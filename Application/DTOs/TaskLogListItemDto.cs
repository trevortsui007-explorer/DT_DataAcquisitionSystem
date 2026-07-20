using System;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class TaskLogListItemDto
    {
        public string TaskLogId { get; set; }
        public int TaskId { get; set; }
        public string TaskCode { get; set; }
        public string TriggerType { get; set; }
        public string Status { get; set; }
        public int TotalConfigs { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int ProcessedCount { get; set; }
        public int Progress { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Message { get; set; }
    }

    public class TaskLogWarningSummaryRequestDto
    {
        public string[] TaskLogIds { get; set; }
    }

    public class TaskLogWarningSummaryDto
    {
        public string TaskLogId { get; set; }
        public int WarningCount { get; set; }
    }
}
