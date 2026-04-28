using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using Learun.Application.WebApi.Modules.DT_DataAcquisitionSystem.Application.DTOs;

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

        public async Task<int> GetNextStartRowAsync(int configId, string fileName, CancellationToken ct = default)
        {
            int lastPos = await _logRepo.GetLastProcessedRowByConfigIdAsync(configId, fileName, ct).ConfigureAwait(false);
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

        /// <summary>
        /// Dashboard 按时间范围读取任务总日志。
        /// </summary>
        public async Task<List<DashboardTaskLogDto>> GetDashboardTaskLogsAsync(DateTime startTime, DateTime endTime, int? limit = null)
        {
            if (endTime <= startTime)
            {
                return new List<DashboardTaskLogDto>();
            }
            return await _logRepo.GetDashboardTaskLogsAsync(startTime, endTime, limit);
        }
    }
}