using DT_DataAcquisitionSystem.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class PostProcessingContext
    {
        public AcquisitionConfig Config { get; set; }

        public string TaskLogId { get; set; }

        public DateTime BusinessDate { get; set; }

        public string SourceTableName { get; set; }

        public string PostTableName { get; set; }

        public string FileName { get; set; }

        public string FullPath { get; set; }

        public IReadOnlyList<PostProcessingRowKey> Rows { get; set; }
    }

    public class PostProcessingRowKey
    {
        public Guid Id { get; set; }

        public string FullPath { get; set; }

        public int? Row { get; set; }
    }

    public interface IPostProcessingService
    {
        /// <summary>
        /// 根据配置中的 PostProcessingType 调用不同的处理逻辑
        /// </summary>
        Task ProcessAsync(AcquisitionConfig config, CancellationToken ct);

        Task ProcessAsync(PostProcessingContext context, CancellationToken ct);
    }
}
