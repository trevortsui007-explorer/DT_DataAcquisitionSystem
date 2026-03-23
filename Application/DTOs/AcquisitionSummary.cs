using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class AcquisitionSummary
    {
        public int TotalCount => SuccessCount + FailureCount;
        public int SuccessCount;
        public int FailureCount;
        public List<string> ErrorDetails { get; set; } = new List<string>();
    }
}