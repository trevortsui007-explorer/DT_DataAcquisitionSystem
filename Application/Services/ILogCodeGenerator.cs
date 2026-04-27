using System.Threading;
using System.Threading.Tasks;

namespace DT_DataAcquisitionSystem.Application.Services
{
    /// <summary>
    /// 任务可视编号生成器
    /// 规则：ACQ-{TriggerType}-{yyyyMMdd}-{D4}
    /// 例如：ACQ-MAN-20260426-0001
    /// </summary>
    public interface ILogCodeGenerator
    {
        /// <summary>
        /// 生成任务编号
        /// </summary>
        /// <param name="triggerType">触发类型：MAN / SCH</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>任务编号</returns>
        Task<string> GenerateTaskCodeAsync(string triggerType, CancellationToken ct = default);
    }
}