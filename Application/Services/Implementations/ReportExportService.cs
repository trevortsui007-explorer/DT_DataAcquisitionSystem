using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Infrastructure.Export;
using DT_DataAcquisitionSystem.Infrastructure.Repositories;
using Hangfire;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class ReportExportService : IReportExportService
    {
        private const int MaxExportDays = 7;
        private readonly IReportExportTaskRepository _taskRepository;
        private readonly IReportExportDataProvider _dataProvider;
        private readonly IReportWorkbookWriter _workbookWriter;
        private readonly IReportArchiveService _archiveService;

        public ReportExportService()
            : this(
                new ReportExportTaskRepository(),
                new ReportExportDataRepository(),
                new NpoiReportWorkbookWriter(),
                new ReportArchiveService())
        {
        }

        public ReportExportService(
            IReportExportTaskRepository taskRepository,
            IReportExportDataProvider dataProvider,
            IReportWorkbookWriter workbookWriter,
            IReportArchiveService archiveService)
        {
            _taskRepository = taskRepository;
            _dataProvider = dataProvider;
            _workbookWriter = workbookWriter;
            _archiveService = archiveService;
        }

        public Task<ReportExportCreateResponseDto> CreateTaskAsync(ReportExportCreateRequestDto request)
        {
            ValidateRequest(request);
            _taskRepository.EnsureStorage();

            var groupIds = request.GroupIds.Select(id => id).Distinct().OrderBy(id => id).ToList();
            var groupDefinitions = _dataProvider.GetGroupDefinitions(groupIds);

            if (groupDefinitions.Count != groupIds.Count)
            {
                var foundIds = new HashSet<int>(groupDefinitions.Select(g => g.GroupId));
                var missingIds = groupIds.Where(id => !foundIds.Contains(id));
                throw new InvalidOperationException("未找到配置组: " + string.Join(",", missingIds));
            }

            var groupsWithoutProcedure = groupDefinitions
                .Where(g => string.IsNullOrWhiteSpace(g.ExportProcedureName))
                .Select(g => $"{g.GroupName}({g.GroupId})")
                .ToList();
            if (groupsWithoutProcedure.Any())
            {
                throw new InvalidOperationException("以下配置组未配置导出存储过程: " + string.Join(", ", groupsWithoutProcedure));
            }

            var startTime = request.StartTime;
            var endTime = NormalizeEndTime(request.EndTime);
            var now = DateTime.Now;
            var task = new ReportExportTask
            {
                Id = Guid.NewGuid().ToString("N"),
                GroupIds = string.Join(",", groupIds),
                StartTime = startTime,
                EndTime = endTime,
                Status = ReportExportTaskStatus.Queued,
                Progress = 0,
                Stage = "排队中",
                CreatedAt = now,
                ExpiredAt = now.AddHours(24)
            };

            _taskRepository.Create(task);

            try
            {
                BackgroundJob.Enqueue(() => ReportExportJob.Execute(task.Id));
            }
            catch (Exception ex)
            {
                _taskRepository.Fail(task.Id, "Hangfire 后台任务提交失败: " + ex.Message);
                throw new InvalidOperationException("Hangfire 后台任务未成功启动，请检查 OWIN Startup 和 Hangfire 存储配置。", ex);
            }

            return Task.FromResult(new ReportExportCreateResponseDto { ExportTaskId = task.Id });
        }

        public ReportExportTaskDto GetTask(string id)
        {
            _taskRepository.EnsureStorage();
            var task = _taskRepository.GetById(id);
            if (task == null) return null;

            return new ReportExportTaskDto
            {
                Id = task.Id,
                Status = task.Status,
                Progress = task.Progress,
                Stage = task.Stage,
                FileName = task.FileName,
                ErrorMessage = task.ErrorMessage,
                CanDownload = task.Status == ReportExportTaskStatus.Success && File.Exists(task.FilePath),
                CreatedAt = task.CreatedAt,
                ExpiredAt = task.ExpiredAt
            };
        }

        public string GetDownloadFilePath(string id, out string fileName)
        {
            _taskRepository.EnsureStorage();
            var task = _taskRepository.GetById(id);
            if (task == null) throw new InvalidOperationException("导出任务不存在。");
            if (task.Status != ReportExportTaskStatus.Success) throw new InvalidOperationException("导出任务尚未完成。");
            if (task.ExpiredAt < DateTime.Now) throw new InvalidOperationException("导出文件已过期。");
            if (string.IsNullOrWhiteSpace(task.FilePath) || !File.Exists(task.FilePath)) throw new FileNotFoundException("导出文件不存在。", task.FilePath);
            fileName = task.FileName;
            return task.FilePath;
        }

        public void ExecuteTask(string id)
        {
            _taskRepository.EnsureStorage();
            var task = _taskRepository.GetById(id);
            if (task == null) return;

            try
            {
                _taskRepository.UpdateProgress(id, ReportExportTaskStatus.Running, 5, "准备导出");
                CleanupExpiredFiles();

                var groupIds = task.GroupIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
                var groupDefinitions = _dataProvider.GetGroupDefinitions(groupIds).ToList();
                var taskFolder = CreateTaskFolder(task.Id);
                var excelFiles = new List<string>();

                for (int i = 0; i < groupDefinitions.Count; i++)
                {
                    var group = groupDefinitions[i];
                    int baseProgress = 10 + (int)(70.0 * i / Math.Max(1, groupDefinitions.Count));
                    _taskRepository.UpdateProgress(id, ReportExportTaskStatus.Running, baseProgress, $"执行存储过程: {group.GroupName}");

                    var dataSets = _dataProvider.ExecuteGroupReport(group.GroupId, group.ExportProcedureName, task.StartTime, task.EndTime);
                    string excelPath = Path.Combine(taskFolder, CreateSafeFileName($"{group.GroupName}_{task.StartTime:yyyyMMdd}_{task.EndTime:yyyyMMdd}.xlsx"));

                    _taskRepository.UpdateProgress(id, ReportExportTaskStatus.Running, Math.Min(90, baseProgress + 10), $"写入 Excel: {group.GroupName}");
                    _workbookWriter.Write(excelPath, dataSets);
                    excelFiles.Add(excelPath);
                }

                string finalPath;
                string finalName;
                if (excelFiles.Count == 1)
                {
                    finalPath = excelFiles[0];
                    finalName = Path.GetFileName(finalPath);
                }
                else
                {
                    _taskRepository.UpdateProgress(id, ReportExportTaskStatus.Running, 94, "压缩文件");
                    finalName = $"报表导出_{task.StartTime:yyyyMMdd}_{task.EndTime:yyyyMMdd}.zip";
                    finalPath = Path.Combine(taskFolder, finalName);
                    _archiveService.CreateZip(finalPath, excelFiles);
                }

                _taskRepository.Complete(id, finalPath, finalName);
            }
            catch (Exception ex)
            {
                _taskRepository.Fail(id, ex.Message);
                throw;
            }
        }

        private static void ValidateRequest(ReportExportCreateRequestDto request)
        {
            if (request == null) throw new InvalidOperationException("请求体不能为空。");
            if (request.GroupIds == null || !request.GroupIds.Any()) throw new InvalidOperationException("请至少选择一个配置组。");
            if (request.StartTime == default(DateTime) || request.EndTime == default(DateTime)) throw new InvalidOperationException("开始时间和结束时间不能为空。");
            var startTime = request.StartTime;
            var endTime = NormalizeEndTime(request.EndTime);
            if (endTime < startTime) throw new InvalidOperationException("结束时间不能早于开始时间。");
            int days = (endTime.Date - startTime.Date).Days + 1;
            if (days > MaxExportDays) throw new InvalidOperationException($"单次报表导出最多支持 {MaxExportDays} 天。");
        }

        private static DateTime NormalizeEndTime(DateTime endTime)
        {
            if (endTime.Second == 0 && endTime.Millisecond == 0)
            {
                return endTime.AddSeconds(59);
            }

            return endTime;
        }

        private static string CreateTaskFolder(string taskId)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ReportExports", taskId);
            Directory.CreateDirectory(root);
            return root;
        }

        private static string CreateSafeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        private static void CleanupExpiredFiles()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ReportExports");
            if (!Directory.Exists(root)) return;

            foreach (var dir in Directory.GetDirectories(root))
            {
                try
                {
                    if (Directory.GetCreationTime(dir).AddHours(24) < DateTime.Now)
                    {
                        Directory.Delete(dir, true);
                    }
                }
                catch
                {
                    // 清理失败不影响本次导出。
                }
            }
        }
    }

    public static class ReportExportJob
    {
        public static void Execute(string taskId)
        {
            new ReportExportService().ExecuteTask(taskId);
        }
    }
}

