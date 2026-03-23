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
    }
}