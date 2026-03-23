using DT_DataAcquisitionSystem.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IPostProcessingService
    {
        /// <summary>
        /// 根据配置中的 PostProcessingType 调用不同的处理逻辑
        /// </summary>
        Task ProcessAsync(AcquisitionConfig config, CancellationToken ct);
    }
}