namespace DT_DataAcquisitionSystem.Domain.Entities
{
    /// <summary>
    /// 后处理方式枚举
    /// </summary>
    public enum PostProcessingType
    {
        None = 0,       // 无后处理
        Service = 1,    // 使用Service
        Procedure = 2   // 使用存储过程

    }
}