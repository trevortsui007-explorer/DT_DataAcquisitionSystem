using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class AcquisitionExecutionService : IAcquisitionExecutionService
    {
        private readonly IDataAcquisitionService _dataAcquisitionService;
        private readonly IAcquisitionLogService _acquisitionLogService;
        private readonly IFileConfigService _fileConfigService;

        public AcquisitionExecutionService(
            IDataAcquisitionService dataAcquisitionService,
            IAcquisitionLogService acquisitionLogService,
            IFileConfigService fileConfigService)
        {
            _dataAcquisitionService = dataAcquisitionService ?? throw new ArgumentNullException(nameof(dataAcquisitionService));
            _acquisitionLogService = acquisitionLogService ?? throw new ArgumentNullException(nameof(acquisitionLogService));
            _fileConfigService = fileConfigService ?? throw new ArgumentNullException(nameof(fileConfigService));
        }

        public async Task<TaskStartResponseDto> StartByIdsAsync(string[] ids, DateTime processDate, CancellationToken ct = default)
        {
            var configs = await GetConfigsByIdsAsync(ids, ct).ConfigureAwait(false);
            return await StartBatchAsync(configs, processDate.Date, processDate.Date, "采集任务已启动。", ct).ConfigureAwait(false);
        }

        public async Task<TaskStartResponseDto> StartByGroupsAsync(string[] groupIds, DateTime processDate, CancellationToken ct = default)
        {
            var configs = await GetConfigsByGroupIdsAsync(groupIds, ct).ConfigureAwait(false);
            return await StartBatchAsync(configs, processDate.Date, processDate.Date, "分组采集任务已启动。", ct).ConfigureAwait(false);
        }

        public async Task<TaskStartResponseDto> StartByTasksAsync(string[] taskIds, DateTime processDate, CancellationToken ct = default)
        {
            var configs = await GetConfigsByTaskIdsAsync(taskIds, ct).ConfigureAwait(false);
            return await StartBatchAsync(configs, processDate.Date, processDate.Date, "计划任务采集已启动。", ct).ConfigureAwait(false);
        }

        public async Task<TaskStartResponseDto> StartByRangeAsync(AcquisitionConfig config, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            ValidateDateRange(startDate, endDate);

            var configs = new List<AcquisitionConfig> { config };
            return await StartBatchAsync(configs, startDate.Date, endDate.Date, "区间采集任务已启动。", ct).ConfigureAwait(false);
        }

        public async Task<TaskStartResponseDto> StartConfigsByRangeAsync(FileConfigQueryOptions options, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            ValidateDateRange(startDate, endDate);

            var configs = await GetConfigsByOptionsAsync(options, ct).ConfigureAwait(false);
            return await StartBatchAsync(configs, startDate.Date, endDate.Date, "批量区间采集任务已启动。", ct).ConfigureAwait(false);
        }

        public async Task<TaskStatusDto> GetTaskStatusAsync(string taskLogId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            var taskLog = await _acquisitionLogService.GetTaskLogByIdAsync(taskLogId, ct).ConfigureAwait(false);
            if (taskLog == null)
            {
                return null;
            }

            return new TaskStatusDto
            {
                TaskLogId = taskLog.Id,
                Status = taskLog.Status,
                TotalConfigs = taskLog.TotalConfigs,
                SuccessCount = taskLog.SuccessCount,
                FailureCount = taskLog.FailureCount,
                ProcessedCount = taskLog.ProcessedCount,
                Progress = taskLog.Progress,
                StartTime = taskLog.StartTime,
                EndTime = taskLog.EndTime,
                Message = taskLog.Message
            };
        }

        public async Task<List<TaskDetailLogDto>> GetTaskDetailsAsync(string taskLogId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            var logs = await _acquisitionLogService.GetLogsByTaskLogIdAsync(taskLogId, ct).ConfigureAwait(false);

            return (logs ?? new List<AcquisitionLogEntry>())
                .Select(x => new TaskDetailLogDto
                {
                    Id = x.Id,
                    TaskLogId = x.TaskLogId,
                    ConfigId = x.ConfigId,
                    FileName = x.FileName,
                    StartRow = x.StartRow,
                    ProcessedRows = x.ProcessedRows,
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    Status = x.Status,
                    ErrorMessage = x.ErrorMessage
                })
                .ToList();
        }

        private async Task<TaskStartResponseDto> StartBatchAsync(
            List<AcquisitionConfig> configs,
            DateTime startDate,
            DateTime endDate,
            string successMessage,
            CancellationToken ct)
        {
            configs = configs ?? new List<AcquisitionConfig>();

            if (configs.Count == 0)
            {
                return new TaskStartResponseDto
                {
                    TaskLogId = null,
                    Status = "NoData",
                    Message = "未找到可执行的采集配置。"
                };
            }

            int totalCount = configs.Count * ((endDate.Date - startDate.Date).Days + 1);

            var taskLogEntry = new AcquisitionTaskLogEntry
            {
                TaskId = 0,
                StartTime = DateTime.Now,
                EndTime = null,
                Status = "Running",
                TotalConfigs = totalCount,
                SuccessCount = 0,
                FailureCount = 0,
                ProcessedCount = 0,
                Progress = 0,
                Message = "任务已创建，等待执行。"
            };

            string taskLogId = await _acquisitionLogService.RecordTaskLogEntryAsync(taskLogEntry, ct).ConfigureAwait(false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _dataAcquisitionService.ExecuteBatchWithTaskLogAsync(
                        configs,
                        startDate,
                        endDate,
                        taskLogId,
                        CancellationToken.None
                    ).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await _acquisitionLogService.CompleteTaskAsync(
                        taskLogId,
                        "Failed",
                        totalCount,
                        0,
                        totalCount,
                        $"任务执行器异常终止：{ex.Message}",
                        CancellationToken.None
                    ).ConfigureAwait(false);
                }
            });

            return new TaskStartResponseDto
            {
                TaskLogId = taskLogId,
                Status = "Running",
                Message = successMessage
            };
        }

        private Task<List<AcquisitionConfig>> GetConfigsByIdsAsync(string[] ids, CancellationToken ct)
        {
            if (ids == null || ids.Length == 0)
                return Task.FromResult(new List<AcquisitionConfig>());

            var result = _fileConfigService.GetByIds(ids);
            return Task.FromResult(result?.ToList() ?? new List<AcquisitionConfig>());
        }

        private Task<List<AcquisitionConfig>> GetConfigsByGroupIdsAsync(string[] groupIds, CancellationToken ct)
        {
            if (groupIds == null || groupIds.Length == 0)
                return Task.FromResult(new List<AcquisitionConfig>());

            var result = _fileConfigService.GetConfigsByGroupIds(groupIds);
            return Task.FromResult(result?.ToList() ?? new List<AcquisitionConfig>());
        }

        private Task<List<AcquisitionConfig>> GetConfigsByTaskIdsAsync(string[] taskIds, CancellationToken ct)
        {
            if (taskIds == null || taskIds.Length == 0)
                return Task.FromResult(new List<AcquisitionConfig>());

            var result = _fileConfigService.GetConfigsByTaskIds(taskIds);
            return Task.FromResult(result?.ToList() ?? new List<AcquisitionConfig>());
        }

        private Task<List<AcquisitionConfig>> GetConfigsByOptionsAsync(FileConfigQueryOptions options, CancellationToken ct)
        {
            var result = _fileConfigService.GetFileConfigs(options);
            return Task.FromResult(result?.ToList() ?? new List<AcquisitionConfig>());
        }

        private static void ValidateDateRange(DateTime startDate, DateTime endDate)
        {
            if (endDate.Date < startDate.Date)
            {
                throw new ArgumentException("结束时间不能早于开始时间。");
            }
        }
    }
}