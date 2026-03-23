namespace DT_DataAcquisitionSystem.Domain.Entities
{
    /// <summary>
    /// 后处理方式枚举
    /// </summary>
    public enum PostProcessingType
    {
        None = 0,       // 无后处理
        Procedure = 1,  // 使用存储过程
        Service = 2     // 使用Service
    }
}