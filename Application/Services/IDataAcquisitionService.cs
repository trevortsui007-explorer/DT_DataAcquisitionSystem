using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;
using Nancy;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IDataAcquisitionService
    {
        // 任务采集
        Task<AcquisitionSummary> ProcessByTask(string[] taskids, DateTime processdate, CancellationToken ct = default);

        // 配置组采集
        Task<AcquisitionSummary> ProcessByGroup(string[] groupIds, DateTime processDate, CancellationToken ct = default);

        // 配置采集
        Task<AcquisitionSummary> ProcessByIds(string[] ids, DateTime processDate, CancellationToken ct = default);

        // 时间段采集
        Task<AcquisitionSummary> ProcessByTimeRange(AcquisitionConfig config, DateTime startDate, DateTime endDate, CancellationToken ct = default);

        // 时间段多配置采集
        Task<AcquisitionSummary> ProcessConfigsByTimeRange(FileConfigQueryOptions options, DateTime startDate, DateTime endDate, CancellationToken ct = default);

        // 单个配置单日执行
        Task<Response> ProcessSingleConfig(AcquisitionConfig config, DateTime processDate, CancellationToken ct = default);

        // 单个配置单日执行（带 taskLogId，用于写明细日志）
        Task ProcessSingleConfig(AcquisitionConfig config, DateTime processDate, string taskLogId, CancellationToken ct = default);


        // 执行器外部已创建 taskLogId，由这里负责真正批量执行并实时更新进度
        Task<AcquisitionSummary> ExecuteBatchWithTaskLogAsync(IEnumerable<AcquisitionConfig> configs, DateTime start, DateTime end, string taskLogId, CancellationToken ct = default);
    }
}