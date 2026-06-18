using System;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class ReportExportCreateRequestDto
    {
        public List<int> GroupIds { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }

    public class ReportExportCreateResponseDto
    {
        public string ExportTaskId { get; set; }
    }

    public class ReportExportTaskDto
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public int Progress { get; set; }
        public string Stage { get; set; }
        public string FileName { get; set; }
        public string ErrorMessage { get; set; }
        public bool CanDownload { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiredAt { get; set; }
    }
}
