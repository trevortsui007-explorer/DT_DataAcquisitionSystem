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
        /// 获取指定配置与文件最后一次处理到的行号，用于断点续传
        /// </summary>
        Task<int> GetLastProcessedRowByConfigIdAsync(int configId, string fileName, CancellationToken ct = default);
    }
}