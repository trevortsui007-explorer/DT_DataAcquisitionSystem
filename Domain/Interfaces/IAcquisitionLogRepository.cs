using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface IAcquisitionLogRepository
    {
        /// <summary>
        /// 异步插入单条采集明细日志（DA_AcquisitionLog）
        /// </summary>
        Task<string> InsertAsync(AcquisitionLogEntry entry, CancellationToken ct = default);

        /// <summary>
        /// 异步插入任务总日志（DA_AcquisitionTaskLog）
        /// </summary>
        Task<string> InsertAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default);

        /// <summary>
        /// 异步更新任务总日志（任务完成时使用，更新结束时间和最终状态）
        /// </summary>
        Task<bool> UpdateAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default);

        /// <summary>
        /// 异步更新任务总日志运行中进度（任务执行中实时刷新）
        /// </summary>
        Task<bool> UpdateProgressAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default);

        /// <summary>
        /// 按任务日志 Id 查询任务总日志（DA_AcquisitionTaskLog）
        /// </summary>
        Task<AcquisitionTaskLogEntry> GetTaskLogByIdAsync(string taskLogId, CancellationToken ct = default);

        /// <summary>
        /// 按任务日志 Id 查询任务明细日志列表（DA_AcquisitionLog）
        /// </summary>
        Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, CancellationToken ct = default);

        /// <summary>
        /// 按任务日志 Id 分页查询任务明细日志列表（DA_AcquisitionLog）
        /// </summary>
        Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, int pageNo, int pageSize, string status = null, CancellationToken ct = default);

        /// <summary>
        /// 按任务日志 Id 获取任务明细日志总数（支持状态筛选）
        /// </summary>
        Task<int> GetLogsCountByTaskLogIdAsync(string taskLogId, string status = null, CancellationToken ct = default);

        /// <summary>
        /// 获取指定配置与文件最后一次处理到的行号，用于断点续传
        /// </summary>
        Task<int> GetLastProcessedRowByConfigIdAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default);

        /// <summary>
        /// 获取采集任务日志列表（支持分页与多条件筛选）
        /// </summary>
        Task<List<AcquisitionTaskLogEntry>> GetTaskLogsAsync(int pageNo, int pageSize, string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default);

        /// <summary>
        /// 获取采集任务日志总数（用于分页计数）
        /// </summary>
        Task<int> GetTaskLogsCountAsync(string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default);
    }

    public interface IAcquisitionFileStateRepository
    {
        Task<AcquisitionFileState> GetAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default);

        Task<List<AcquisitionFileState>> GetByConfigAndDateRangeAsync(int configId, DateTime startDate, DateTime endDate, CancellationToken ct = default);

        Task<bool> UpsertSuccessAsync(AcquisitionFileState state, bool allowSealedUpdate, CancellationToken ct = default);

        Task<int> SealByTaskLogAsync(string taskLogId, CancellationToken ct = default);
    }
}
