using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IAcquisitionExecutionService
    {
        Task<TaskStartResponseDto> StartByIdsAsync(string[] ids, DateTime processDate, CancellationToken ct = default);
        Task<TaskStartResponseDto> StartByGroupsAsync(string[] groupIds, DateTime processDate, CancellationToken ct = default);
        Task<TaskStartResponseDto> StartByTasksAsync(string[] taskIds, DateTime processDate, CancellationToken ct = default);

        /// <summary> 定时任务入口 </summary>
        Task<TaskStartResponseDto> StartScheduledByTasksAsync(string[] taskIds, DateTime processDate, CancellationToken ct = default);

        Task<TaskStartResponseDto> StartByRangeAsync(AcquisitionConfig config, DateTime startDate, DateTime endDate, CancellationToken ct = default);
        Task<TaskStartResponseDto> StartConfigsByRangeAsync(FileConfigQueryOptions options, DateTime startDate, DateTime endDate, CancellationToken ct = default);
        Task<TaskStartResponseDto> CancelTaskAsync(string taskLogId, CancellationToken ct = default);

        Task<TaskStatusDto> GetTaskStatusAsync(string taskLogId, CancellationToken ct = default);
        Task<List<TaskDetailLogDto>> GetTaskDetailsAsync(string taskLogId, CancellationToken ct = default);
        Task<PagedResultDto<TaskDetailLogDto>> GetTaskDetailsAsync(string taskLogId, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);
        Task<PagedResultDto<TaskConfigDetailGroupDto>> GetTaskConfigDetailGroupsAsync(string taskLogId, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);
        Task<PagedResultDto<ConfigHistoryTaskGroupDto>> GetConfigHistoryAsync(int configId, DateTime? startTime, DateTime? endTime, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);
        Task<List<ConfigHistorySummaryDto>> GetConfigHistorySummaryAsync(IEnumerable<int> configIds, DateTime? startTime = null, DateTime? endTime = null, CancellationToken ct = default);
        Task<PagedResultDto<ConfigFileStateDto>> GetConfigFileStatesAsync(int configId, DateTime? startTime, DateTime? endTime, int pageNo, int pageSize, string status = null, bool hasProcessedRows = false, CancellationToken ct = default);
        Task<List<ConfigFileStateSummaryDto>> GetConfigFileStateSummaryAsync(IEnumerable<int> configIds, DateTime? startTime = null, DateTime? endTime = null, CancellationToken ct = default);
        Task<TaskDetailSummaryDto> GetTaskDetailSummaryAsync(string taskLogId, CancellationToken ct = default);

        /// <summary> 分页获取采集任务日志列表（支持状态、时间范围及任务 ID 筛选） </summary>
        Task<PagedResultDto<TaskLogListItemDto>> GetTaskLogsAsync(int pageNo, int pageSize, string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default);

        /// <summary> 批量获取历史任务文件缺失 Warning 汇总 </summary>
        Task<List<TaskLogWarningSummaryDto>> GetTaskLogWarningSummaryAsync(IEnumerable<string> taskLogIds, CancellationToken ct = default);
    }
}

