using System;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    using System.Collections.Generic;

    public class TaskDetailLogDto
    {
        public string Id { get; set; }
        public string TaskLogId { get; set; }
        public int ConfigId { get; set; }
        public string FileName { get; set; }
        public string FullFilePath { get; set; }
        public int StartRow { get; set; }
        public int ProcessedRows { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorCategory { get; set; }
        public string ErrorCategoryName { get; set; }
    }

    public class TaskConfigDetailGroupDto
    {
        public int ConfigId { get; set; }
        public string ConfigName { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int WarningFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ProcessedRows { get; set; }
        public List<TaskDetailLogDto> LatestDetails { get; set; } = new List<TaskDetailLogDto>();
    }

    public class ConfigHistoryTaskGroupDto
    {
        public string TaskLogId { get; set; }
        public string TaskCode { get; set; }
        public string TriggerType { get; set; }
        public string TaskStatus { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int WarningFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ProcessedRows { get; set; }
        public List<TaskDetailLogDto> LatestDetails { get; set; } = new List<TaskDetailLogDto>();
    }

    public class ConfigHistorySummaryDto
    {
        public int ConfigId { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int WarningFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ProcessedRows { get; set; }
    }

    public class ConfigFileStateDto
    {
        public long Id { get; set; }
        public int ConfigId { get; set; }
        public DateTime BusinessDate { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public int DataRowCount { get; set; }
        public int LastStartRow { get; set; }
        public int LastProcessedRows { get; set; }
        public string LastTaskLogId { get; set; }
        public string LastStatus { get; set; }
        public string LastUpdateSource { get; set; }
        public bool IsSealed { get; set; }
        public DateTime? SealTime { get; set; }
        public DateTime? LastScanTime { get; set; }
        public DateTime? LastWriteTime { get; set; }
        public DateTime? LastWriteTimeUtc { get; set; }
        public long? FileSize { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
    }

    public class ConfigFileStateSummaryDto
    {
        public int ConfigId { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ProcessedRows { get; set; }
        public int NewFiles { get; set; }
    }

    public class ConfigFileStateSummaryRequestDto
    {
        public int[] ConfigIds { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
    }

    public class ConfigHistorySummaryRequestDto
    {
        public int[] ConfigIds { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
    }
}

