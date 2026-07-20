namespace DT_DataAcquisitionSystem.Application.DTOs
{
    using System.Collections.Generic;

    public class TaskDetailSummaryDto
    {
        public string TaskLogId { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int WarningFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ProcessedRows { get; set; }
        public List<TaskErrorCategorySummaryDto> ErrorCategories { get; set; } = new List<TaskErrorCategorySummaryDto>();
    }

    public class TaskErrorCategorySummaryDto
    {
        public string Category { get; set; }
        public string CategoryName { get; set; }
        public int Count { get; set; }
        public decimal Percent { get; set; }
    }
}
