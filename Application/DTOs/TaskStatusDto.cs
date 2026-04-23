using System;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class TaskStatusDto
    {
        public string TaskLogId { get; set; }
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
}