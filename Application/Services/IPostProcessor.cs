using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IPostProcessor
    {
        // 对应配置表里的 ServiceName
        string ProcessorName { get; }

        // 执行逻辑：传入 flag 和配置信息
        Task ExecuteAsync(string flag, AcquisitionConfig config, CancellationToken ct);

        Task ExecuteAsync(PostProcessingContext context, CancellationToken ct);
    }
}
