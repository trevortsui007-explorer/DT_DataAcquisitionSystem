using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Domain.Entities
{
    /// <summary>
    /// 采集配置：业务聚合根（包含所有文件相关配置）
    /// </summary>
    public class AcquisitionConfig
    {
        public int Id { get; set; }
        public string EqName { get; set; } // 机台名称（唯一业务标识）
        public string TableName { get; set; } // 业务表名
        public string FilePathPattern { get; set; } // 文件根路径（模板，如 ftp://server/A1/{yyyy}-{M}/）
        public string FileNamePattern { get; set; } // 文件名模板（如 record-{yyyy}-{M}-{d}.csv）
        public string FileType { get; set; } // 文件类型（.csv/.xlsx/.txt等）
        public int HeaderRow { get; set; } // 表头行号
        public int StartRow { get; set; } // 数据起始行号
        public string FieldMappings { get; set; } // 映射表（JSON字符串）
        public string ExtFields { get; set; } // 额外字段（逗号分隔）
        public bool IsEnabled { get; set; } // 是否启用配置
        public PostProcessingType PostProcessingType { get; set; } // 后处理方式（枚举）
        public string PostTableName { get; set; } // 后处理存入表（逗号分隔）
        public string ProcedureName { get; set; } // 存储过程名（当PostProcessingType=Procedure时有效）
        public string ServiceName { get; set; } // Service名（当PostProcessingType=Service时有效）
        public string Flag { get; set; } // 业务标识（如 processFailurePointInfo）
        public string FlagName { get; set; } // 业务标识名称（如 "处理坏点信息"）
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// 业务方法：解析映射表
        /// </summary>
        public Dictionary<string, string> ParseFieldMappings()
        {
            if (string.IsNullOrEmpty(FieldMappings))
            {
                return new Dictionary<string, string>();
            }
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(FieldMappings);
        }
    }
}