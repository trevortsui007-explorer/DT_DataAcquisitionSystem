namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class AcquisitionGroupDto
    {
        public string Id { get; set; }
        public string GroupName { get; set; }
        public string GroupCategory { get; set; }
        public string GroupType { get; set; }
        public int SortOrder { get; set; }
        public int IsEnable { get; set; }
        public int ConfigCount { get; set; }
    }
}