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
        Task<TaskStartResponseDto> StartByRangeAsync(AcquisitionConfig config, DateTime startDate, DateTime endDate, CancellationToken ct = default);
        Task<TaskStartResponseDto> StartConfigsByRangeAsync(FileConfigQueryOptions options, DateTime startDate, DateTime endDate, CancellationToken ct = default);

        Task<TaskStatusDto> GetTaskStatusAsync(string taskLogId, CancellationToken ct = default);
        Task<List<TaskDetailLogDto>> GetTaskDetailsAsync(string taskLogId, CancellationToken ct = default);
    }
}