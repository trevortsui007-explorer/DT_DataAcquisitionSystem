namespace DT_DataAcquisitionSystem.Domain.Entities
{
    public class AcquisitionGroupConfig
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public int ConfigId { get; set; }
        public bool IsEnabled { get; set; }
    }
}