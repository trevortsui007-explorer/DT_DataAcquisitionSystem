using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Learun.Application.WebApi.Modules.DT_DataAcquisitionSystem.Application.DTOs
{
    /// <summary>
    /// Dashboard 趋势图点位 DTO。
    /// 前端需要小写字段：time/value。
    /// </summary>
    public class DashboardTrendDto
    {
        public string time { get; set; }

        public int value { get; set; } 
    }

    /// <summary>
    /// Dashboard 活动日志 DTO。
    /// 前端需要小写字段：time/text/level。
    /// </summary>
    public class DashboardActivityDto
    {
        public string time { get; set; }
        public string text { get; set; }
        public string level { get; set; }
    }

    /// <summary>
    /// Dashboard 查询 DA_AcquisitionTaskLog 使用的原始日志 DTO。
    /// 只取页面统计需要的字段，避免读取大字段或无关字段。
    /// </summary>
    public class DashboardTaskLogDto
    {
        public DateTime ? StartTime { get; set; }
        public string TaskId { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public int ? ProcessedCount { get; set; }
        public int ? SuccessCount { get; set; }
        public int ? FailureCount { get; set; }
    }


}