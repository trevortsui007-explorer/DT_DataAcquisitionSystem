using System.Threading.Tasks;
using System.Threading;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface IAcquisitionLogRepository
    {
        /// <summary>
        /// 异步插入单条采集日志
        /// </summary>
        Task<string> InsertAsync(AcquisitionLogEntry entry, CancellationToken ct = default);

        /// <summary>
        /// 异步插入任务执行日志
        /// </summary>
        Task<string> InsertAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default);
        
        /// <summary>
        /// 异步更新任务执行日志
        /// </summary>
        Task<bool> UpdateAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default);
        
        /// <summary>
        /// 获取指定【配置】【同一日期】最后一次处理到的行号
        /// </summary>
        Task<int> GetLastProcessedRowByConfigIdAsync(int configId, string fileName, CancellationToken ct = default);
    }
}