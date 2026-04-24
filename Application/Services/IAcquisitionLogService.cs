using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IAcquisitionLogService
    {
        // 获取下一次采集的起始行
        Task<int> GetNextStartRowAsync(int configId, string fileName, CancellationToken ct = default);

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
}