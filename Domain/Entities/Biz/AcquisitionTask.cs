using System;

namespace DT_DataAcquisitionSystem.Domain.Entities
{
    /// <summary>
    /// 采集任务主表：定义执行计划
    /// </summary>
    public class AcquisitionTask
    {
        public int Id { get; set; }
        public string TaskName { get; set; }

        /// <summary>
        /// 采集模式（0: 手动/暂停，1: 自动周期等）
        /// </summary>
        public byte TaskMode { get; set; }

        public string CronExpression { get; set; }
        public int IsEnabled { get; set; }
        public int PostProcessingTiming { get; set; }
        public string Description { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
    }
}