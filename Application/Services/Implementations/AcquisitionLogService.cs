using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Infrastructure.Repositories;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class AcquisitionLogService : IAcquisitionLogService
    {
        private readonly IFileConfigRepository _configRepo;
        private readonly IAcquisitionLogRepository _logRepo;

        public AcquisitionLogService(IFileConfigRepository configRepo, IAcquisitionLogRepository logRepo)
        {
            _configRepo = configRepo;
            _logRepo = logRepo;
        }

        public async Task<int> GetNextStartRowAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default)
        {
            int lastPos = await _logRepo.GetLastProcessedRowByConfigIdAsync(configId, businessDate.Date, fileName, ct).ConfigureAwait(false);
            if (lastPos > 0) return lastPos;

            var ids = new List<string> { configId.ToString() };
            var configList = _configRepo.GetListByIds(ids);
            var config = configList?.FirstOrDefault();

            return config?.StartRow ?? 0;
        }

        public async Task<string> RecordLogEntryAsync(AcquisitionLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            if (entry.ConfigId <= 0 || string.IsNullOrWhiteSpace(entry.TaskLogId))
            {
                throw new InvalidOperationException("记录明细日志失败：ConfigId 必须大于 0，且 TaskLogId 不能为空。");
            }

            if (!string.IsNullOrWhiteSpace(entry.Status))
            {
                entry.Status = NormalizeStatus(entry.Status);
            }

            return await _logRepo.InsertAsync(entry, ct).ConfigureAwait(false);
        }

        public async Task<string> RecordTaskLogEntryAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            if (entry.TaskId < 0)
            {
                throw new InvalidOperationException("记录任务总日志失败：TaskId 必须 >= 0。");
            }

            if (!string.IsNullOrWhiteSpace(entry.TaskCode))
            {
                entry.TaskCode = entry.TaskCode.Trim();

                if (entry.TaskCode.Length > 50)
                {
                    throw new InvalidOperationException("记录任务总日志失败：TaskCode 长度不能超过 50。");
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.TriggerType))
            {
                entry.TriggerType = entry.TriggerType.Trim().ToUpperInvariant();

                if (entry.TriggerType != TaskTriggerTypes.Manual &&
                    entry.TriggerType != TaskTriggerTypes.Scheduled &&
                    entry.TriggerType != TaskTriggerTypes.Test)
                {
                    throw new InvalidOperationException("记录任务总日志失败：TriggerType 只能是 MAN、SCH 或 TST。");
                }
            }

            entry.Status = NormalizeStatus(entry.Status);
            entry.ProcessedCount = entry.SuccessCount + entry.FailureCount;
            entry.Progress = CalculateProgress(entry.TotalConfigs, entry.SuccessCount, entry.FailureCount);

            if (!string.IsNullOrWhiteSpace(entry.Message))
            {
                entry.Message = entry.Message.Trim();
            }

            return await _logRepo.InsertAsync(entry, ct).ConfigureAwait(false);
        }

        public async Task<bool> UpdateProgressAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.Id)) throw new ArgumentNullException(nameof(entry.Id));

            entry.Status = NormalizeStatus(entry.Status);
            entry.ProcessedCount = entry.SuccessCount + entry.FailureCount;
            entry.Progress = CalculateProgress(entry.TotalConfigs, entry.SuccessCount, entry.FailureCount);

            if (!string.IsNullOrWhiteSpace(entry.Message))
            {
                entry.Message = entry.Message.Trim();
            }

            return await _logRepo.UpdateProgressAsync(entry, ct).ConfigureAwait(false);
        }

        public async Task<bool> UpdateTaskProgressAsync(string id, string status, int totalConfigs, int successCount, int failureCount, string message = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var entry = new AcquisitionTaskLogEntry
            {
                Id = id,
                Status = NormalizeStatus(status),
                TotalConfigs = totalConfigs,
                SuccessCount = successCount,
                FailureCount = failureCount,
                ProcessedCount = successCount + failureCount,
                Progress = CalculateProgress(totalConfigs, successCount, failureCount),
                Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim()
            };

            return await _logRepo.UpdateProgressAsync(entry, ct).ConfigureAwait(false);
        }

        public async Task<bool> CompleteTaskAsync(string id, string status, int totalConfigs, int successCount, int failureCount, string message = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            var processedCount = successCount + failureCount;
            var progress = totalConfigs <= 0 ? 100 : CalculateProgress(totalConfigs, successCount, failureCount);

            if (totalConfigs > 0 && processedCount >= totalConfigs)
            {
                progress = 100;
            }

            var entry = new AcquisitionTaskLogEntry
            {
                Id = id,
                Status = NormalizeStatus(status),
                TotalConfigs = totalConfigs,
                SuccessCount = successCount,
                FailureCount = failureCount,
                ProcessedCount = processedCount,
                Progress = progress,
                Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
                EndTime = DateTime.Now
            };

            return await _logRepo.UpdateAsync(entry, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 分页查询任务总日志列表
        /// </summary>
        public async Task<List<AcquisitionTaskLogEntry>> GetTaskLogsAsync(int pageNo, int pageSize, string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default)
        {
            int safePageNo = pageNo <= 0 ? 1 : pageNo;
            int safePageSize = pageSize <= 0 ? 20 : pageSize;

            if (safePageSize > 200)
            {
                safePageSize = 200;
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                status = status.Trim();
            }

            var result = await _logRepo.GetTaskLogsAsync(safePageNo, safePageSize, status, startTime, endTime, taskId, ct).ConfigureAwait(false);

            return result ?? new List<AcquisitionTaskLogEntry>();
        }

        /// <summary>
        /// 查询任务总日志总数
        /// </summary>
        public async Task<int> GetTaskLogsCountAsync(string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(status))
            {
                status = status.Trim();
            }

            return await _logRepo.GetTaskLogsCountAsync(status, startTime, endTime, taskId, ct).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, int>> GetTaskLogWarningCountsAsync(IEnumerable<string> taskLogIds, CancellationToken ct = default)
        {
            var ids = (taskLogIds ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToArray();

            if (ids.Length == 0)
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            return await _logRepo.GetTaskLogWarningCountsAsync(ids, ct).ConfigureAwait(false)
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        public async Task<AcquisitionTaskLogEntry> GetTaskLogByIdAsync(string taskLogId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            return await _logRepo.GetTaskLogByIdAsync(taskLogId, ct).ConfigureAwait(false);
        }

        public async Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            return await _logRepo.GetLogsByTaskLogIdAsync(taskLogId, ct).ConfigureAwait(false);
        }

        public async Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            return await _logRepo.GetLogsByTaskLogIdAsync(taskLogId, pageNo, pageSize, status, errorCategory, hasProcessedRows, ct).ConfigureAwait(false);
        }

        public async Task<int> GetLogsCountByTaskLogIdAsync(string taskLogId, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            return await _logRepo.GetLogsCountByTaskLogIdAsync(taskLogId, status, errorCategory, hasProcessedRows, ct).ConfigureAwait(false);
        }

        public async Task<List<AcquisitionLogConfigGroup>> GetLogConfigGroupsByTaskLogIdAsync(string taskLogId, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            return await _logRepo.GetLogConfigGroupsByTaskLogIdAsync(taskLogId, pageNo, pageSize, status, errorCategory, hasProcessedRows, ct).ConfigureAwait(false);
        }

        public async Task<int> GetLogConfigGroupsCountByTaskLogIdAsync(string taskLogId, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            return await _logRepo.GetLogConfigGroupsCountByTaskLogIdAsync(taskLogId, status, errorCategory, hasProcessedRows, ct).ConfigureAwait(false);
        }

        public async Task<List<AcquisitionLogEntry>> GetLatestLogsByTaskLogIdAndConfigIdsAsync(string taskLogId, IEnumerable<int> configIds, int takePerConfig = 10, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            var ids = (configIds ?? Enumerable.Empty<int>())
                .Where(x => x > 0)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
            {
                return new List<AcquisitionLogEntry>();
            }

            return await _logRepo.GetLatestLogsByTaskLogIdAndConfigIdsAsync(taskLogId, ids, takePerConfig, status, errorCategory, hasProcessedRows, ct).ConfigureAwait(false);
        }

        public async Task<List<AcquisitionLogConfigTaskGroup>> GetLogConfigHistoryTaskGroupsAsync(int configId, DateTime? startTime, DateTime? endTime, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (configId <= 0)
                throw new ArgumentOutOfRangeException(nameof(configId));

            return await _logRepo.GetLogConfigHistoryTaskGroupsAsync(configId, startTime, endTime, pageNo, pageSize, status, errorCategory, hasProcessedRows, ct).ConfigureAwait(false);
        }

        public async Task<int> GetLogConfigHistoryTaskGroupsCountAsync(int configId, DateTime? startTime, DateTime? endTime, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (configId <= 0)
                throw new ArgumentOutOfRangeException(nameof(configId));

            return await _logRepo.GetLogConfigHistoryTaskGroupsCountAsync(configId, startTime, endTime, status, errorCategory, hasProcessedRows, ct).ConfigureAwait(false);
        }

        public async Task<List<AcquisitionLogEntry>> GetLatestLogsByConfigIdAndTaskLogIdsAsync(int configId, IEnumerable<string> taskLogIds, int takePerTask = 10, DateTime? startTime = null, DateTime? endTime = null, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (configId <= 0)
                throw new ArgumentOutOfRangeException(nameof(configId));

            var ids = (taskLogIds ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ids.Length == 0)
            {
                return new List<AcquisitionLogEntry>();
            }

            return await _logRepo.GetLatestLogsByConfigIdAndTaskLogIdsAsync(configId, ids, takePerTask, startTime, endTime, status, errorCategory, hasProcessedRows, ct).ConfigureAwait(false);
        }

        public async Task<List<AcquisitionLogConfigHistorySummary>> GetLogConfigHistorySummariesAsync(IEnumerable<int> configIds, DateTime? startTime = null, DateTime? endTime = null, CancellationToken ct = default)
        {
            var ids = (configIds ?? Enumerable.Empty<int>())
                .Where(x => x > 0)
                .Distinct()
                .Take(500)
                .ToArray();

            if (ids.Length == 0)
            {
                return new List<AcquisitionLogConfigHistorySummary>();
            }

            return await _logRepo.GetLogConfigHistorySummariesAsync(ids, startTime, endTime, ct).ConfigureAwait(false);
        }

        public async Task<int> GetLogsProcessedRowsByTaskLogIdAsync(string taskLogId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            return await _logRepo.GetLogsProcessedRowsByTaskLogIdAsync(taskLogId, ct).ConfigureAwait(false);
        }

        public async Task<bool> UpdateTaskStatusAsync(string id, string status, int successCount, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var entry = new AcquisitionTaskLogEntry
            {
                Id = id,
                Status = NormalizeStatus(status),
                SuccessCount = successCount,
                FailureCount = 0,
                ProcessedCount = successCount,
                Progress = 100,
                EndTime = DateTime.Now
            };

            return await _logRepo.UpdateAsync(entry, ct).ConfigureAwait(false);
        }

        private string NormalizeStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "Running";
            }

            switch (status.Trim().ToLowerInvariant())
            {
                case "running":
                    return "Running";
                case "success":
                    return "Success";
                case "failed":
                case "failure":
                    return "Failed";
                case "partialsuccess":
                case "partial_success":
                case "partial success":
                    return "PartialSuccess";
                case "nodata":
                case "no_data":
                case "no data":
                    return "NoData";
                default:
                    return status.Trim();
            }
        }

        private int CalculateProgress(int totalConfigs, int successCount, int failureCount)
        {
            if (totalConfigs <= 0) return 0;

            var processedCount = successCount + failureCount;
            if (processedCount <= 0) return 0;

            var progress = (int)Math.Floor(processedCount * 100.0 / totalConfigs);

            if (progress < 0) return 0;
            if (progress > 100) return 100;

            return progress;
        }
    }

    public class AcquisitionFileStateService : IAcquisitionFileStateService
    {
        private readonly IAcquisitionFileStateRepository _fileStateRepository;

        public AcquisitionFileStateService()
            : this(new AcquisitionFileStateRepository())
        {
        }

        public AcquisitionFileStateService(IAcquisitionFileStateRepository fileStateRepository)
        {
            _fileStateRepository = fileStateRepository ?? throw new ArgumentNullException(nameof(fileStateRepository));
        }

        public Task<AcquisitionFileState> GetAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default)
        {
            if (configId <= 0 || string.IsNullOrWhiteSpace(fileName))
            {
                return Task.FromResult<AcquisitionFileState>(null);
            }

            return _fileStateRepository.GetAsync(configId, businessDate.Date, fileName.Trim(), ct);
        }

        public async Task<bool> ShouldSkipForSealedAsync(int configId, DateTime businessDate, string fileName, string updateSource, CancellationToken ct = default)
        {
            if (configId <= 0 || string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (IsManualRepair(updateSource))
            {
                return false;
            }

            var state = await _fileStateRepository
                .GetAsync(configId, businessDate.Date, fileName.Trim(), ct)
                .ConfigureAwait(false);

            return state != null && state.IsSealed;
        }

        public async Task<bool> UpsertSuccessAsync(AcquisitionConfig config, DateTime businessDate, string fullPath, AcquisitionLogEntry logEntry, string updateSource, FileMetadata fileMetadata = null, bool allowSealedUpdate = false, CancellationToken ct = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (logEntry == null) throw new ArgumentNullException(nameof(logEntry));
            if (string.IsNullOrWhiteSpace(logEntry.FileName)) throw new ArgumentNullException(nameof(logEntry.FileName));

            string safeSource = NormalizeUpdateSource(updateSource);
            int baseStartRow = config.StartRow <= 0 ? 1 : config.StartRow;
            int dataRowCount = Math.Max(0, logEntry.StartRow - baseStartRow + logEntry.ProcessedRows);

            var state = new AcquisitionFileState
            {
                ConfigId = config.Id,
                BusinessDate = businessDate.Date,
                FileName = logEntry.FileName.Trim(),
                FullPath = string.IsNullOrWhiteSpace(fullPath) ? null : fullPath.Trim(),
                DataRowCount = dataRowCount,
                LastStartRow = logEntry.StartRow,
                LastProcessedRows = logEntry.ProcessedRows,
                LastTaskLogId = logEntry.TaskLogId,
                LastStatus = "Success",
                LastUpdateSource = safeSource,
                LastWriteTime = fileMetadata?.LastWriteTime,
                LastWriteTimeUtc = fileMetadata?.LastWriteTimeUtc,
                FileSize = fileMetadata?.Length
            };

            return await _fileStateRepository
                .UpsertSuccessAsync(state, IsManualRepair(safeSource) || allowSealedUpdate, ct)
                .ConfigureAwait(false);
        }

        public Task<int> SealByTaskLogAsync(string taskLogId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
            {
                return Task.FromResult(0);
            }

            return _fileStateRepository.SealByTaskLogAsync(taskLogId.Trim(), ct);
        }

        public Task<List<AcquisitionFileState>> GetByConfigAndDateRangeAsync(int configId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            if (configId <= 0)
            {
                return Task.FromResult(new List<AcquisitionFileState>());
            }

            return _fileStateRepository.GetByConfigAndDateRangeAsync(configId, startDate.Date, endDate.Date, ct);
        }

        public Task<List<AcquisitionFileState>> GetPagedByConfigAndDateRangeAsync(int configId, DateTime startDate, DateTime endDate, int pageNo, int pageSize, string status = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (configId <= 0)
            {
                return Task.FromResult(new List<AcquisitionFileState>());
            }

            return _fileStateRepository.GetPagedByConfigAndDateRangeAsync(configId, startDate.Date, endDate.Date, pageNo, pageSize, status, hasProcessedRows, ct);
        }

        public Task<int> GetCountByConfigAndDateRangeAsync(int configId, DateTime startDate, DateTime endDate, string status = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            if (configId <= 0)
            {
                return Task.FromResult(0);
            }

            return _fileStateRepository.GetCountByConfigAndDateRangeAsync(configId, startDate.Date, endDate.Date, status, hasProcessedRows, ct);
        }

        public async Task<List<ConfigFileStateSummaryDto>> GetSummaryByConfigIdsAsync(IEnumerable<int> configIds, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            var summaries = await _fileStateRepository
                .GetSummaryByConfigIdsAsync(configIds, startDate.Date, endDate.Date, ct)
                .ConfigureAwait(false);

            return (summaries ?? new List<AcquisitionFileStateSummary>())
                .Select(x => new ConfigFileStateSummaryDto
                {
                    ConfigId = x.ConfigId,
                    TotalFiles = x.TotalFiles,
                    SuccessFiles = x.SuccessFiles,
                    FailedFiles = x.FailedFiles,
                    ProcessedRows = x.ProcessedRows,
                    NewFiles = x.NewFiles
                })
                .ToList();
        }
        private static string NormalizeUpdateSource(string updateSource)
        {
            return string.IsNullOrWhiteSpace(updateSource)
                ? FileStateUpdateSources.ManualCurrent
                : updateSource.Trim().ToUpperInvariant();
        }

        private static bool IsManualRepair(string updateSource)
        {
            return string.Equals(
                NormalizeUpdateSource(updateSource),
                FileStateUpdateSources.ManualRepair,
                StringComparison.Ordinal);
        }
    }
}

