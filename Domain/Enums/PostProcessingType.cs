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

    public enum PostProcessingTiming
    {
        PerFile = 0,    // 每个文件入库后立即执行后处理
        AfterTask = 1   // 任务采集完成后统一执行一次后处理
    }
}