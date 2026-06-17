using System;
using System.Collections.Concurrent;
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
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _serviceLocks
            = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        public PostProcessingService(
            IDataService dataService,
            IEnumerable<IPostProcessor> customProcessors)
        {
            _dataService = dataService;
            _customProcessors = customProcessors;
        }

        public async Task ProcessAsync(AcquisitionConfig config, CancellationToken ct)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            await ProcessAsync(new PostProcessingContext
            {
                Config = config,
                BusinessDate = DateTime.Today,
                SourceTableName = config.TableName,
                PostTableName = config.PostTableName,
                Rows = new List<PostProcessingRowKey>()
            }, ct).ConfigureAwait(false);
        }

        public async Task ProcessAsync(PostProcessingContext context, CancellationToken ct)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Config == null) throw new ArgumentNullException(nameof(context.Config));

            var config = context.Config;
            context.SourceTableName = string.IsNullOrWhiteSpace(context.SourceTableName)
                ? config.TableName
                : context.SourceTableName;
            context.PostTableName = string.IsNullOrWhiteSpace(context.PostTableName)
                ? config.PostTableName
                : context.PostTableName;
            context.Rows = context.Rows ?? new List<PostProcessingRowKey>();

            switch (config.PostProcessingType)
            {
                case PostProcessingType.None:
                    return;

                case PostProcessingType.Procedure:
                    await HandleProcedure(config, ct).ConfigureAwait(false);
                    break;

                case PostProcessingType.Service:
                    await HandleCustomService(context, ct).ConfigureAwait(false);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task HandleProcedure(AcquisitionConfig config, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(config.ProcedureName))
                throw new Exception($"配置项 {config.EqName} 设置为存储过程后处理，但 ProcedureName 为空");

            await _dataService.ExecuteStoredProcedureAsync(config.Flag, config.ProcedureName, ct).ConfigureAwait(false);
        }

        private async Task HandleCustomService(PostProcessingContext context, CancellationToken ct)
        {
            var config = context.Config;
            if (string.IsNullOrEmpty(config.ServiceName))
                throw new Exception($"配置项 {config.EqName} 设置为 Service 后处理，但 ServiceName 为空");

            var processor = _customProcessors.FirstOrDefault(p => p.ProcessorName == config.ServiceName);

            if (processor == null)
                throw new Exception($"未找到名为 {config.ServiceName} 的后处理器实现");

            string lockKey = string.Join("|", new[]
            {
                config.ServiceName ?? string.Empty,
                context.SourceTableName ?? string.Empty,
                context.PostTableName ?? string.Empty
            });

            var semaphore = _serviceLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await processor.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
