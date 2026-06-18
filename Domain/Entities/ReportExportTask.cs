using System;

namespace DT_DataAcquisitionSystem.Domain.Entities
{
    public class ReportExportTask
    {
        public string Id { get; set; }
        public string GroupIds { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Status { get; set; }
        public int Progress { get; set; }
        public string Stage { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiredAt { get; set; }
    }

    public static class ReportExportTaskStatus
    {
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Success = "Success";
        public const string Failed = "Failed";
    }
}
