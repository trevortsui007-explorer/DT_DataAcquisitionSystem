namespace DT_DataAcquisitionSystem.Application.Services
{
    /// <summary>
    /// 任务触发类型常量
    /// </summary>
    public static class TaskTriggerTypes
    {
        /// <summary>
        /// 手动触发
        /// </summary>
        public const string Manual = "MAN";

        /// <summary>
        /// 定时任务触发
        /// </summary>
        public const string Scheduled = "SCH";

        /// <summary>
        /// 测试采集触发
        /// </summary>
        public const string Test = "TST";
    }

    /// <summary>
    /// 文件状态表更新来源。
    /// </summary>
    public static class FileStateUpdateSources
    {
        public const string ScheduledCurrent = "SCH_CURRENT";
        public const string ScheduledD1Backfill = "SCH_D1_BACKFILL";
        public const string ManualCurrent = "MAN_CURRENT";
        public const string ManualRepair = "MAN_REPAIR";
    }
}
