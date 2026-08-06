using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Application.DTOs;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IAcquisitionLogService
    {
        Task<int> GetNextStartRowAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default);

        Task<string> RecordTaskLogEntryAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default);

        Task<string> RecordLogEntryAsync(AcquisitionLogEntry entry, CancellationToken ct = default);

        Task<bool> UpdateTaskStatusAsync(string id, string status, int successCount, CancellationToken ct = default);

        Task<AcquisitionTaskLogEntry> GetTaskLogByIdAsync(string taskLogId, CancellationToken ct = default);

        Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, CancellationToken ct = default);

        Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<int> GetLogsCountByTaskLogIdAsync(string taskLogId, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<List<AcquisitionLogConfigGroup>> GetLogConfigGroupsByTaskLogIdAsync(string taskLogId, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<int> GetLogConfigGroupsCountByTaskLogIdAsync(string taskLogId, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<List<AcquisitionLogEntry>> GetLatestLogsByTaskLogIdAndConfigIdsAsync(string taskLogId, IEnumerable<int> configIds, int takePerConfig = 10, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<List<AcquisitionLogConfigTaskGroup>> GetLogConfigHistoryTaskGroupsAsync(int configId, DateTime? startTime, DateTime? endTime, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<int> GetLogConfigHistoryTaskGroupsCountAsync(int configId, DateTime? startTime, DateTime? endTime, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<List<AcquisitionLogEntry>> GetLatestLogsByConfigIdAndTaskLogIdsAsync(int configId, IEnumerable<string> taskLogIds, int takePerTask = 10, DateTime? startTime = null, DateTime? endTime = null, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<List<AcquisitionLogConfigHistorySummary>> GetLogConfigHistorySummariesAsync(IEnumerable<int> configIds, DateTime? startTime = null, DateTime? endTime = null, CancellationToken ct = default);

        Task<int> GetLogsProcessedRowsByTaskLogIdAsync(string taskLogId, CancellationToken ct = default);

        Task<bool> UpdateProgressAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default);

        Task<bool> UpdateTaskProgressAsync(string id, string status, int totalConfigs, int successCount, int failureCount, string message = null, CancellationToken ct = default);

        Task<bool> CompleteTaskAsync(string id, string status, int totalConfigs, int successCount, int failureCount, string message = null, CancellationToken ct = default);

        Task<List<AcquisitionTaskLogEntry>> GetTaskLogsAsync(int pageNo, int pageSize, string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default);

        Task<int> GetTaskLogsCountAsync(string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default);

        Task<Dictionary<string, int>> GetTaskLogWarningCountsAsync(IEnumerable<string> taskLogIds, CancellationToken ct = default);
    }

    public interface IAcquisitionFileStateService
    {
        Task<AcquisitionFileState> GetAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default);

        Task<bool> ShouldSkipForSealedAsync(int configId, DateTime businessDate, string fileName, string updateSource, CancellationToken ct = default);

        Task<bool> UpsertSuccessAsync(AcquisitionConfig config, DateTime businessDate, string fullPath, AcquisitionLogEntry logEntry, string updateSource, FileMetadata fileMetadata = null, bool allowSealedUpdate = false, CancellationToken ct = default);

        Task<int> SealByTaskLogAsync(string taskLogId, CancellationToken ct = default);

        Task<List<AcquisitionFileState>> GetByConfigAndDateRangeAsync(int configId, DateTime startDate, DateTime endDate, CancellationToken ct = default);

        Task<List<AcquisitionFileState>> GetPagedByConfigAndDateRangeAsync(int configId, DateTime startDate, DateTime endDate, int pageNo, int pageSize, string status = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<int> GetCountByConfigAndDateRangeAsync(int configId, DateTime startDate, DateTime endDate, string status = null, bool hasProcessedRows = false, CancellationToken ct = default);

        Task<List<ConfigFileStateSummaryDto>> GetSummaryByConfigIdsAsync(IEnumerable<int> configIds, DateTime startDate, DateTime endDate, CancellationToken ct = default);
    }
}

