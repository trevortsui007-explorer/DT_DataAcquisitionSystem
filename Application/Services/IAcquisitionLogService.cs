using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IAcquisitionLogService
    {
        // 获取下一次采集的起始行
        Task<int> GetNextStartRowAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default);

        // 记录本次采集任务进度
        Task<string> RecordTaskLogEntryAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default);
        
        // 记录本次配置采集进度
        Task<string> RecordLogEntryAsync(AcquisitionLogEntry entry, CancellationToken ct = default);

        // 更新本次采集任务进度
        Task<bool> UpdateTaskStatusAsync(string id, string status, int successCount, CancellationToken ct = default);

        // 获取TaskLog记录
        Task<AcquisitionTaskLogEntry> GetTaskLogByIdAsync(string taskLogId, CancellationToken ct = default);

        // 获取TaskLogs记录
        Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, CancellationToken ct = default);

        // 分页获取TaskLogs记录
        Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, int pageNo, int pageSize, string status = null, CancellationToken ct = default);

        // 获取TaskLogs记录总数
        Task<int> GetLogsCountByTaskLogIdAsync(string taskLogId, string status = null, CancellationToken ct = default);

        // 运行中实时更新
        Task<bool> UpdateProgressAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default);

        // 更新任务进度
        Task<bool> UpdateTaskProgressAsync(string id, string status, int totalConfigs, int successCount, int failureCount, string message = null, CancellationToken ct = default);

        // 更新任务最终状态
        Task<bool> CompleteTaskAsync(string id, string status, int totalConfigs, int successCount, int failureCount, string message = null, CancellationToken ct = default);

        /// <summary> 分页获取采集任务日志原始条目列表（支持多条件筛选） </summary>
        Task<List<AcquisitionTaskLogEntry>> GetTaskLogsAsync(int pageNo, int pageSize, string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default);

        /// <summary> 获取符合筛选条件的采集任务日志总数 </summary>
        Task<int> GetTaskLogsCountAsync(string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default);
    }

    public interface IAcquisitionFileStateService
    {
        Task<AcquisitionFileState> GetAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default);

        Task<bool> ShouldSkipForSealedAsync(int configId, DateTime businessDate, string fileName, string updateSource, CancellationToken ct = default);

        Task<bool> UpsertSuccessAsync(AcquisitionConfig config, DateTime businessDate, string fullPath, AcquisitionLogEntry logEntry, string updateSource, FileMetadata fileMetadata = null, bool allowSealedUpdate = false, CancellationToken ct = default);

        Task<int> SealByTaskLogAsync(string taskLogId, CancellationToken ct = default);

        Task<List<AcquisitionFileState>> GetByConfigAndDateRangeAsync(int configId, DateTime startDate, DateTime endDate, CancellationToken ct = default);
    }
}
