namespace DT_DataAcquisitionSystem.Domain.Entities
{
    /// <summary>
    /// 配置分类组：如麦逊组、麦崇组
    /// </summary>
    public class AcquisitionGroup
    {
        public int Id { get; set; }
        public string GroupName { get; set; }
        public string GroupCategory { get; set; }
        public string GroupType { get; set; }
        public int SortOrder { get; set; }
        public bool IsEnabled { get; set; }
    }
}