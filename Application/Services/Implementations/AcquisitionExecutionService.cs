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
        private readonly ILogCodeGenerator _logCodeGenerator;

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
            return await StartBatchAsync(configs, processDate.Date, processDate.Date, TaskTriggerTypes.Manual, "计划任务采集已启动。", ct).ConfigureAwait(false);
        }
        public async Task<TaskStartResponseDto> StartScheduledByTasksAsync(string[] taskIds, DateTime processDate, CancellationToken ct = default)
        {
            var configs = await GetConfigsByTaskIdsAsync(taskIds, ct).ConfigureAwait(false);
            return await StartBatchAsync(configs, processDate.Date, processDate.Date, TaskTriggerTypes.Scheduled, "计划任务采集已启动。", ct).ConfigureAwait(false);
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
                .Select(x => new TaskDetailLogDto
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
                    ErrorMessage = x.ErrorMessage
                })
                .ToList();
        }

        public async Task<PagedResultDto<TaskDetailLogDto>> GetTaskDetailsAsync(string taskLogId, int pageNo, int pageSize, string status = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            int safePageNo = pageNo <= 0 ? 1 : pageNo;
            int safePageSize = pageSize <= 0 ? 10 : pageSize;

            if (safePageSize > 200)
            {
                safePageSize = 200;
            }

            var logs = await _acquisitionLogService.GetLogsByTaskLogIdAsync(
                taskLogId,
                safePageNo,
                safePageSize,
                status,
                ct).ConfigureAwait(false);

            var total = await _acquisitionLogService.GetLogsCountByTaskLogIdAsync(
                taskLogId,
                status,
                ct).ConfigureAwait(false);

            var items = (logs ?? new List<AcquisitionLogEntry>())
                .Select(x => new TaskDetailLogDto
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
                    ErrorMessage = x.ErrorMessage
                })
                .ToList();

            return new PagedResultDto<TaskDetailLogDto>
            {
                Items = items,
                Total = total,
                PageNo = safePageNo,
                PageSize = safePageSize
            };
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

        private async Task<TaskStartResponseDto> StartBatchAsync(
            List<AcquisitionConfig> configs,
            DateTime startDate,
            DateTime endDate,
            string triggerType,
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

            var taskLogEntry = await CreateRunningTaskLogAsync(
                totalCount,
                triggerType,
                "任务已创建，等待执行。",
                ct).ConfigureAwait(false);

            string taskLogId = await _acquisitionLogService.RecordTaskLogEntryAsync(taskLogEntry, ct).ConfigureAwait(false);
            string updateSource = ResolveUpdateSource(triggerType, startDate, endDate);
            bool sealOnSuccess = updateSource == FileStateUpdateSources.ScheduledD1Backfill;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _dataAcquisitionService.ExecuteBatchWithTaskLogAsync(
                        configs,
                        startDate,
                        endDate,
                        taskLogId,
                        CancellationToken.None,
                        updateSource,
                        sealOnSuccess
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
