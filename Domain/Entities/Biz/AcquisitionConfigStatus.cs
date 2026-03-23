namespace DT_DataAcquisitionSystem.Domain.Entities
{
    public class AcquisitionConfigStatus
    {
        public int Id { get; set; }
        public string Name { get; set; } // 名称
        public bool IsEnabled { get; set; } // 是否启用配置

    }
}