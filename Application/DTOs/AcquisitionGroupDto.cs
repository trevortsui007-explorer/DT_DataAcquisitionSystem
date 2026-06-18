using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class AcquisitionGroupDto
    {
        public string Id { get; set; }
        public string GroupName { get; set; }
        public string GroupCategory { get; set; }
        public string GroupType { get; set; }
        public string ExportProcedureName { get; set; }
        public int IsEnabled { get; set; }
        public int ConfigCount { get; set; }
        public List<AcquisitionConfigDto> AssociatedConfigs { get; set; }
    }
}
