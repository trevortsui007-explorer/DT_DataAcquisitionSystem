using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Common.Utilities;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class AcquisitionExecutionService : IAcquisitionExecutionService
    {
        private readonly IDataAcquisitionService _dataAcquisitionService;
        private readonly IAcquisitionLogService _acquisitionLogService;
        private readonly IFileConfigService _fileConfigService;
        private readonly ILogCodeGenerator _logCodeGenerator;
        private readonly DT_DataAcquisitionSystem.Domain.Interfaces.IAcquisitionTaskService _taskService;
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> RunningTasks =
            new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);

        public AcquisitionExecutionService(
            IDataAcquisitionService dataAcquisitionService,
            IAcquisitionLogService acquisitionLogService,
            IFileConfigService fileConfigService,
            ILogCodeGenerator logCodeGenerator)
        {
            _dataAcquisitionService = dataAcquisitionService ?? throw new ArgumentNullException(nameof(dataAcquisitionService));
            _acquisitionLogService = acquisitionLogService ?? throw new ArgumentNullException(nameof(acquisitionLogService));
            _fileConfigService = fileConfigService ?? throw new ArgumentNullException(nameof(fileConfigService));
            _logCodeGenerator = logCodeGenerator ?? throw new ArgumentNullException(nameof(logCodeGenerator));
            try
            {
                _taskService = TaskIocHelper.GetTaskService();
            }
            catch
            {
                _taskService = null;
            }
        }

        public async Task<TaskStartResponseDto> StartByIdsAsync(string[] ids, DateTime processDate, CancellationToken ct = default)
        {
            var configs = await GetConfigsByIdsAsync(ids, ct).ConfigureAwait(false);
            return await StartBatchAsync(configs, processDate.Date, processDate.Date, TaskTriggerTypes.Manual, "采集任务已启动。", ct).ConfigureAwait(false);
        }

        public async Task<TaskStartResponseDto> StartByGroupsAsync(string[] groupIds, DateTime processDate, CancellationToken ct = default)
        {
            var configs = await GetConfigsByGroupIdsAsync(groupIds, ct).ConfigureAwait(false);
            return await StartBatchAsync(configs, processDate.Date, processDate.Date, TaskTriggerTypes.Manual, "分组采集任务已启动。", ct).ConfigureAwait(false);
        }

        public async Task<TaskStartResponseDto> StartByTasksAsync(string[] taskIds, DateTime processDate, CancellationToken ct = default)
        {
            var configs = await GetConfigsByTaskIdsAsync(taskIds, ct).ConfigureAwait(false);
            var postProcessingTiming = ResolvePostProcessingTiming(taskIds);
            return await StartBatchAsync(configs, processDate.Date, processDate.Date, TaskTriggerTypes.Manual, "计划任务采集已启动。", ct, postProcessingTiming).ConfigureAwait(false);
        }
        public async Task<TaskStartResponseDto> StartScheduledByTasksAsync(string[] taskIds, DateTime processDate, CancellationToken ct = default)
        {
            var configs = await GetConfigsByTaskIdsAsync(taskIds, ct).ConfigureAwait(false);
            var postProcessingTiming = ResolvePostProcessingTiming(taskIds);
            return await StartBatchAsync(configs, processDate.Date, processDate.Date, TaskTriggerTypes.Scheduled, "计划任务采集已启动。", ct, postProcessingTiming).ConfigureAwait(false);
        }

        public async Task<TaskStartResponseDto> StartByRangeAsync(AcquisitionConfig config, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            ValidateDateRange(startDate, endDate);

            var configs = new List<AcquisitionConfig> { config };
            return await StartBatchAsync(configs, startDate.Date, endDate.Date, TaskTriggerTypes.Manual, "区间采集任务已启动。", ct).ConfigureAwait(false);
        }

        public async Task<TaskStartResponseDto> StartConfigsByRangeAsync(FileConfigQueryOptions options, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            ValidateDateRange(startDate, endDate);

            var configs = await GetConfigsByOptionsAsync(options, ct).ConfigureAwait(false);
            return await StartBatchAsync(configs, startDate.Date, endDate.Date, TaskTriggerTypes.Manual, "批量区间采集任务已启动。", ct).ConfigureAwait(false);
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
                TaskCode = taskLog.TaskCode,
                TriggerType = taskLog.TriggerType,
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
                .Select(ToTaskDetailLogDto)
                .ToList();
        }

        public async Task<PagedResultDto<TaskDetailLogDto>> GetTaskDetailsAsync(string taskLogId, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            int safePageNo = pageNo <= 0 ? 1 : pageNo;
            int safePageSize = pageSize <= 0 ? 10 : pageSize;

            if (safePageSize > 200)
            {
                safePageSize = 200;
            }

            if (!string.IsNullOrWhiteSpace(errorCategory))
            {
                var allLogs = await _acquisitionLogService.GetLogsByTaskLogIdAsync(taskLogId, ct).ConfigureAwait(false);
                var filteredLogs = (allLogs ?? new List<AcquisitionLogEntry>())
                    .Where(x => IsDetailStatusMatch(x, status))
                    .Where(x => string.Equals(GetErrorCategory(x.Status, x.ErrorMessage), errorCategory, StringComparison.OrdinalIgnoreCase))
                    .Where(x => !hasProcessedRows || x.ProcessedRows > 0)
                    .OrderByDescending(x => x.StartTime)
                    .ThenByDescending(x => x.Id)
                    .ToList();

                var pagedItems = filteredLogs
                    .Skip((safePageNo - 1) * safePageSize)
                    .Take(safePageSize)
                    .Select(ToTaskDetailLogDto)
                    .ToList();

                return new PagedResultDto<TaskDetailLogDto>
                {
                    Items = pagedItems,
                    Total = filteredLogs.Count,
                    PageNo = safePageNo,
                    PageSize = safePageSize
                };
            }

            var logs = await _acquisitionLogService.GetLogsByTaskLogIdAsync(
                taskLogId,
                safePageNo,
                safePageSize,
                status,
                null,
                hasProcessedRows,
                ct).ConfigureAwait(false);

            var total = await _acquisitionLogService.GetLogsCountByTaskLogIdAsync(
                taskLogId,
                status,
                null,
                hasProcessedRows,
                ct).ConfigureAwait(false);

            var items = (logs ?? new List<AcquisitionLogEntry>())
                .Select(ToTaskDetailLogDto)
                .ToList();

            return new PagedResultDto<TaskDetailLogDto>
            {
                Items = items,
                Total = total,
                PageNo = safePageNo,
                PageSize = safePageSize
            };
        }

        public async Task<TaskDetailSummaryDto> GetTaskDetailSummaryAsync(string taskLogId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            var totalTask = _acquisitionLogService.GetLogsCountByTaskLogIdAsync(taskLogId, null, null, false, ct);
            var successTask = _acquisitionLogService.GetLogsCountByTaskLogIdAsync(taskLogId, "Success", null, false, ct);
            var warningTask = _acquisitionLogService.GetLogsCountByTaskLogIdAsync(taskLogId, "Warning", null, false, ct);
            var failedTask = _acquisitionLogService.GetLogsCountByTaskLogIdAsync(taskLogId, "Failed", null, false, ct);
            var processedRowsTask = _acquisitionLogService.GetLogsProcessedRowsByTaskLogIdAsync(taskLogId, ct);
            var logsTask = _acquisitionLogService.GetLogsByTaskLogIdAsync(taskLogId, ct);

            await Task.WhenAll(totalTask, successTask, warningTask, failedTask, processedRowsTask, logsTask)
                .ConfigureAwait(false);

            int errorTotal = warningTask.Result + failedTask.Result;
            var errorCategories = (logsTask.Result ?? new List<AcquisitionLogEntry>())
                .Select(x => GetErrorCategory(x.Status, x.ErrorMessage))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => x)
                .Select(x => new TaskErrorCategorySummaryDto
                {
                    Category = x.Key,
                    CategoryName = GetErrorCategoryName(x.Key),
                    Count = x.Count(),
                    Percent = errorTotal <= 0 ? 0 : Math.Round((decimal)x.Count() * 100 / errorTotal, 2)
                })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Category)
                .ToList();

            return new TaskDetailSummaryDto
            {
                TaskLogId = taskLogId,
                TotalFiles = totalTask.Result,
                SuccessFiles = successTask.Result,
                WarningFiles = warningTask.Result,
                FailedFiles = failedTask.Result,
                ProcessedRows = processedRowsTask.Result,
                ErrorCategories = errorCategories
            };
        }

        private static string GetErrorCategory(string status, string errorMessage)
        {
            if (string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string message = errorMessage ?? string.Empty;

            if (ContainsAny(message, "Post processing failed")) return "PostProcessing";
            if (ContainsAny(message, "\u6587\u4ef6\u672a\u627e\u5230", "\u4e0d\u5b58\u5728", "\u672a\u627e\u5230\u53ef\u5904\u7406\u6587\u4ef6", "File not found")) return "FileMissing";
            if (ContainsAny(message, "SMB \u51ed\u636e", "\u51ed\u636e", "\u7528\u6237\u540d", "\u5bc6\u7801", "\u767b\u5f55\u5931\u8d25", "\u7f51\u7edc\u8def\u5f84", "FTP \u8fde\u63a5")) return "PathCredential";
            if (ContainsAny(message, "\u6b63\u7531\u53e6\u4e00\u8fdb\u7a0b\u4f7f\u7528", "\u88ab\u5360\u7528", "\u62d2\u7edd\u8bbf\u95ee", "Access denied")) return "PermissionLocked";
            if (ContainsAny(message, "\u7f3a\u5c11\u5217", "\u8868\u5934", "\u683c\u5f0f", "\u6a21\u677f", "Sheet")) return "FormatHeader";
            if (ContainsAny(message, "\u89e3\u6790", "\u8f6c\u6362", "DateTime", "Int32", "Decimal", "\u8f93\u5165\u5b57\u7b26\u4e32\u7684\u683c\u5f0f\u4e0d\u6b63\u786e")) return "DataParsing";
            if (ContainsAny(message, "SQL", "\u6570\u636e\u5e93", "INSERT", "\u5b58\u50a8\u8fc7\u7a0b", "\u6b7b\u9501", "\u8fdd\u53cd", "\u622a\u65ad")) return "DatabaseInsert";

            return "Unknown";
        }

        private static TaskDetailLogDto ToTaskDetailLogDto(AcquisitionLogEntry x)
        {
            string category = GetErrorCategory(x.Status, x.ErrorMessage);
            return new TaskDetailLogDto
            {
                Id = x.Id,
                TaskLogId = x.TaskLogId,
                ConfigId = x.ConfigId,
                FileName = x.FileName,
                FullFilePath = x.FullFilePath,
                StartRow = x.StartRow,
                ProcessedRows = x.ProcessedRows,
                StartTime = x.StartTime,
                EndTime = x.EndTime,
                Status = x.Status,
                ErrorMessage = x.ErrorMessage,
                ErrorCategory = category,
                ErrorCategoryName = GetErrorCategoryName(category)
            };
        }

        private static bool IsDetailStatusMatch(AcquisitionLogEntry entry, string status)
        {
            if (string.IsNullOrWhiteSpace(status) || status.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (status.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(GetErrorCategory(entry.Status, entry.ErrorMessage), "FileMissing", StringComparison.OrdinalIgnoreCase);
            }

            if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(entry.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(GetErrorCategory(entry.Status, entry.ErrorMessage), "FileMissing", StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(entry.Status, status, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetErrorCategoryName(string category)
        {
            switch (category)
            {
                case "FileMissing": return "\u6587\u4ef6\u7f3a\u5931";
                case "PathCredential": return "\u8def\u5f84/\u51ed\u636e";
                case "PermissionLocked": return "\u6743\u9650/\u5360\u7528";
                case "FormatHeader": return "\u6587\u4ef6\u683c\u5f0f/\u8868\u5934";
                case "DataParsing": return "\u6570\u636e\u89e3\u6790";
                case "DatabaseInsert": return "\u6570\u636e\u5e93\u5165\u5e93";
                case "PostProcessing": return "\u540e\u5904\u7406";
                case "Unknown": return "\u672a\u5206\u7c7b";
                default: return null;
            }
        }

        private static bool ContainsAny(string value, params string[] keywords)
        {
            return keywords.Any(keyword => value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public async Task<PagedResultDto<TaskLogListItemDto>> GetTaskLogsAsync(int pageNo, int pageSize, string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default)
        {
            int safePageNo = pageNo <= 0 ? 1 : pageNo;
            int safePageSize = pageSize <= 0 ? 20 : pageSize;

            if (safePageSize > 200)
            {
                safePageSize = 200;
            }

            var logs = await _acquisitionLogService.GetTaskLogsAsync(
                safePageNo,
                safePageSize,
                status,
                startTime,
                endTime,
                taskId,
                ct).ConfigureAwait(false);

            var total = await _acquisitionLogService.GetTaskLogsCountAsync(
                status,
                startTime,
                endTime,
                taskId,
                ct).ConfigureAwait(false);

            var items = (logs ?? new List<AcquisitionTaskLogEntry>())
                .Select(x => new TaskLogListItemDto
                {
                    TaskLogId = x.Id,
                    TaskId = x.TaskId,
                    TaskCode = x.TaskCode,
                    TriggerType = x.TriggerType,
                    Status = x.Status,
                    TotalConfigs = x.TotalConfigs,
                    SuccessCount = x.SuccessCount,
                    FailureCount = x.FailureCount,
                    ProcessedCount = x.ProcessedCount,
                    Progress = x.Progress,
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    Message = x.Message
                })
                .ToList();

            return new PagedResultDto<TaskLogListItemDto>
            {
                Items = items,
                Total = total,
                PageNo = safePageNo,
                PageSize = safePageSize
            };
        }

        public async Task<List<TaskLogWarningSummaryDto>> GetTaskLogWarningSummaryAsync(IEnumerable<string> taskLogIds, CancellationToken ct = default)
        {
            var ids = (taskLogIds ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToList();

            if (ids.Count == 0)
            {
                return new List<TaskLogWarningSummaryDto>();
            }

            var counts = await _acquisitionLogService
                .GetTaskLogWarningCountsAsync(ids, ct)
                .ConfigureAwait(false);

            return ids
                .Select(id => new TaskLogWarningSummaryDto
                {
                    TaskLogId = id,
                    WarningCount = counts != null && counts.TryGetValue(id, out var count) ? count : 0
                })
                .ToList();
        }

        public async Task<TaskStartResponseDto> CancelTaskAsync(string taskLogId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            var taskLog = await _acquisitionLogService.GetTaskLogByIdAsync(taskLogId, ct).ConfigureAwait(false);
            if (taskLog == null)
            {
                return new TaskStartResponseDto
                {
                    TaskLogId = taskLogId,
                    Status = "NotFound",
                    Message = "未找到任务日志。"
                };
            }

            if (!string.Equals(taskLog.Status, "Running", StringComparison.OrdinalIgnoreCase))
            {
                return new TaskStartResponseDto
                {
                    TaskLogId = taskLogId,
                    Status = taskLog.Status,
                    Message = "当前任务已结束，无需取消。"
                };
            }

            if (!RunningTasks.TryGetValue(taskLogId, out var cts))
            {
                return new TaskStartResponseDto
                {
                    TaskLogId = taskLogId,
                    Status = "Running",
                    Message = "当前进程未找到运行任务，无法取消。"
                };
            }

            cts.Cancel();

            await _acquisitionLogService.CompleteTaskAsync(
                taskLogId,
                "Cancelled",
                taskLog.TotalConfigs,
                taskLog.SuccessCount,
                taskLog.FailureCount,
                "任务已手动取消",
                ct).ConfigureAwait(false);

            return new TaskStartResponseDto
            {
                TaskLogId = taskLogId,
                Status = "Cancelled",
                Message = "任务已手动取消。"
            };
        }
        private async Task<TaskStartResponseDto> StartBatchAsync(
            List<AcquisitionConfig> configs,
            DateTime startDate,
            DateTime endDate,
            string triggerType,
            string successMessage,
            CancellationToken ct,
            PostProcessingTiming postProcessingTiming = PostProcessingTiming.PerFile)
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

            int totalCount = CalculateWorkItemCount(configs, startDate, endDate);

            var taskLogEntry = await CreateRunningTaskLogAsync(
                totalCount,
                triggerType,
                "任务已创建，等待执行。",
                ct).ConfigureAwait(false);

            string taskLogId = await _acquisitionLogService.RecordTaskLogEntryAsync(taskLogEntry, ct).ConfigureAwait(false);
            string updateSource = ResolveUpdateSource(triggerType, startDate, endDate);
            bool sealOnSuccess = updateSource == FileStateUpdateSources.ScheduledD1Backfill;
            var executionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            RunningTasks[taskLogId] = executionCts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _dataAcquisitionService.ExecuteBatchWithTaskLogAsync(
                        configs,
                        startDate,
                        endDate,
                        taskLogId,
                        executionCts.Token,
                        updateSource,
                        sealOnSuccess,
                        postProcessingTiming
                    ).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    var currentTaskLog = await _acquisitionLogService
                        .GetTaskLogByIdAsync(taskLogId, CancellationToken.None)
                        .ConfigureAwait(false);

                    await _acquisitionLogService.CompleteTaskAsync(
                        taskLogId,
                        "Cancelled",
                        currentTaskLog?.TotalConfigs ?? totalCount,
                        currentTaskLog?.SuccessCount ?? 0,
                        currentTaskLog?.FailureCount ?? 0,
                        "任务已手动取消",
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
                finally
                {
                    RunningTasks.TryRemove(taskLogId, out _);
                    executionCts.Dispose();
                }
            });

            return new TaskStartResponseDto
            {
                TaskLogId = taskLogId,
                Status = "Running",
                Message = successMessage
            };
        }

        private PostProcessingTiming ResolvePostProcessingTiming(string[] taskIds)
        {
            if (taskIds == null || taskIds.Length == 0 || _taskService == null)
            {
                return PostProcessingTiming.PerFile;
            }

            try
            {
                var tasks = _taskService.GetByIds(taskIds)?.ToList() ?? new List<AcquisitionTask>();
                return tasks.Any(x => x.PostProcessingTiming == (int)PostProcessingTiming.AfterTask)
                    ? PostProcessingTiming.AfterTask
                    : PostProcessingTiming.PerFile;
            }
            catch
            {
                return PostProcessingTiming.PerFile;
            }
        }
        private static string ResolveUpdateSource(string triggerType, DateTime startDate, DateTime endDate)
        {
            bool isHistory = endDate.Date < DateTime.Today;

            if (string.Equals(triggerType, TaskTriggerTypes.Scheduled, StringComparison.Ordinal))
            {
                return isHistory
                    ? FileStateUpdateSources.ScheduledD1Backfill
                    : FileStateUpdateSources.ScheduledCurrent;
            }

            return isHistory
                ? FileStateUpdateSources.ManualRepair
                : FileStateUpdateSources.ManualCurrent;
        }

        private static int CalculateWorkItemCount(IEnumerable<AcquisitionConfig> configs, DateTime startDate, DateTime endDate)
        {
            int count = 0;
            var folderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var config in configs ?? Enumerable.Empty<AcquisitionConfig>())
            {
                for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
                {
                    string fileName = FileDateTimeUtil.GetProcessedFileName(config, date);
                    bool folderMode = string.IsNullOrWhiteSpace(fileName);
                    if (!folderMode)
                    {
                        count++;
                        continue;
                    }

                    string path = FileDateTimeUtil.GetProcessedFilePath(config, date);
                    string key = $"{config?.Id ?? 0}|folder|{NormalizePathKey(path)}";
                    if (folderKeys.Add(key))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static string NormalizePathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return path.Trim().TrimEnd('\\', '/').Replace('/', '\\');
        }

        private async Task<AcquisitionTaskLogEntry> CreateRunningTaskLogAsync(int totalCount, string triggerType, string message, CancellationToken ct)
        {
            var taskCode = await _logCodeGenerator.GenerateTaskCodeAsync(triggerType, ct).ConfigureAwait(false);

            return new AcquisitionTaskLogEntry
            {
                TaskId = 0,
                TaskCode = taskCode,
                TriggerType = triggerType,
                StartTime = DateTime.Now,
                EndTime = null,
                Status = "Running",
                TotalConfigs = totalCount,
                SuccessCount = 0,
                FailureCount = 0,
                ProcessedCount = 0,
                Progress = 0,
                Message = message
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
