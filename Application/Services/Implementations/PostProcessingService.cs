using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class PostProcessingService : IPostProcessingService
    {
        private readonly IDataService _dataService;
        private readonly IEnumerable<IPostProcessor> _customProcessors;

        public PostProcessingService(
            IDataService dataService,
            IEnumerable<IPostProcessor> customProcessors)
        {
            _dataService = dataService;
            _customProcessors = customProcessors;
        }

        public async Task ProcessAsync(AcquisitionConfig config, CancellationToken ct)
        {
            switch (config.PostProcessingType)
            {
                case PostProcessingType.None:
                    // 不处理，直接结束
                    return;

                case PostProcessingType.Procedure:
                    await HandleProcedure(config, ct);
                    break;

                case PostProcessingType.Service:
                    await HandleCustomService(config, ct);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // 处理存储过程
        private async Task HandleProcedure(AcquisitionConfig config, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(config.ProcedureName))
                throw new Exception($"配置项 {config.EqName} 设置为存储过程后处理，但 ProcedureName 为空");

            // 调用 DataService 执行存储过程
            // 传入 Flag 作为业务参数
            await _dataService.ExecuteStoredProcedureAsync(config.Flag, config.ProcedureName, ct);
        }

        // 处理 C# 自定义 Service 逻辑
        private async Task HandleCustomService(AcquisitionConfig config, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(config.ServiceName))
                throw new Exception($"配置项 {config.EqName} 设置为 Service 后处理，但 ServiceName 为空");

            var processor = _customProcessors.FirstOrDefault(p => p.ProcessorName == config.ServiceName);

            if (processor == null)
                throw new Exception($"未找到名为 {config.ServiceName} 的后处理器实现");

            await processor.ExecuteAsync(config.Flag, config, ct);
        }
    }
}
