using System;

namespace DT_DataAcquisitionSystem.Domain.Entities
{
    public class FileConfigQueryOptions
    {
        /// <summary>
        /// 指定任务ID数组进行过滤
        /// </summary>
        public string[] TaskIds { get; set; }

        /// <summary>
        /// 指定任务组ID数组进行过滤
        /// </summary>
        public string[] GroupIds { get; set; }
        
        /// <summary>
        /// 指定配置ID号进行过滤
        /// </summary>
        public string[] Ids { get; set; }

        /// <summary>
        /// 配置表名，默认 DA_AcquisitionConfig
        /// </summary>
        public string TableName { get; set; } = "DA_AcquisitionConfig";

        /// <summary>
        /// 组-配置链接表名，默认 DA_AcquisitionConfig
        /// </summary>
        public string LinkTableName { get; set; } = "DA_AcquisitionGroup_Config";

        /// <summary>
        /// 数据库连接名，默认 BaseDb
        /// </summary>
        public string DatabaseName { get; set; } = "BaseDb";

        // 辅助逻辑：判断是否有任务ID过滤
        public bool HasTaskFilter => TaskIds != null && TaskIds.Length > 0;

        // 辅助逻辑：判断是否有组过滤
        public bool HasGroupFilter => GroupIds != null && GroupIds.Length > 0;
        
        // 辅助逻辑：判断是否有组过滤
        public bool HasIdFilter => Ids != null && Ids.Length > 0;
    }
}