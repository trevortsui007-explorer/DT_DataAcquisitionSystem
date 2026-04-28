using System;
using System.Collections.Generic;
using System.Linq;
using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Common.Extensions;
using Learun.Application.WebApi.Modules.DT_DataAcquisitionSystem.Application.DTOs;
using static DT_DataAcquisitionSystem.Common.Extensions.NancyModuleExtensions;

namespace Learun.Application.WebApi.Modules.DT_DataAcquisitionSystem.WebApi.Controllers
{
    /// <summary>
    /// Dashboard 首页接口。
    /// </summary>
    public class DashboardController : BaseApi
    {
        private readonly IAcquisitionLogService _acquisitionLogService;

        public DashboardController(IAcquisitionLogService acquisitionLogService)
              : base("/api/data-acquisition/dashboard")
        {
            _acquisitionLogService = acquisitionLogService ?? throw new ArgumentException("acquisitionLogService");

            Get["/trend", true] = async (parameters, ct) =>
            {
                DateTime todayStatr = DateTime.Today;
                DateTime todayEnd = todayStatr.AddDays(1);

                List<DashboardTaskLogDto> logs = await _acquisitionLogService
                    .GetDashboardTaskLogsAsync(todayStatr, todayEnd, null);

                List<DashboardTrendDto> data = BuildTrendData(todayStatr, logs);

                return this.ToResponse(ResponseCode.success, "获取Dashboard趋势数据成功", data);
            };

            Get["/activities", true] = async (paraments, ct) =>
             {
                 int limit = ReadLimit();

                  DateTime endTime = DateTime.Now;
                  DateTime startTime = endTime.AddHours(-24);

                 List<DashboardTaskLogDto> logs = await _acquisitionLogService
                    .GetDashboardTaskLogsAsync(startTime, endTime, limit);

                 List<DashboardActivityDto> data = logs
                   .Where(x => x != null && x.StartTime.HasValue)
                   .OrderByDescending(x => x.StartTime.Value)
                   .Take(limit)
                   .Select(x => new DashboardActivityDto
                   {
                       time = x.StartTime.Value.ToString("HH:mm"),
                       text = BuildActivityText(x),
                       level = MapActivityLevel(x.Status)
                   })
                   .ToList();

                 return this.ToResponse(ResponseCode.success, "获取Dashboard活动数据成功", data);
             };
        }

        /// <summary>
        /// 构造当天 12 个 2 小时趋势桶。
        /// 即使没有日志，也返回 12 个 value=0 的点，避免前端误判接口异常。
        /// </summary>
        private static List<DashboardTrendDto> BuildTrendData(DateTime todayStart, List<DashboardTaskLogDto> logs)
        {
            List<DashboardTrendDto> result = new List<DashboardTrendDto>();

            for (int i = 0; i < 12; i++)
            {
                DateTime bucketTime = todayStart.AddHours(i * 2);

                result.Add(new DashboardTrendDto
                {
                    time = bucketTime.ToString("HH:mm"),
                    value = 0
                });
            }

            if (logs == null || logs.Count == 0)
            {
                return result;
            }

            foreach (DashboardTaskLogDto log in logs)
            {
                if (log == null || !log.StartTime.HasValue)
                {
                    continue;
                }

                int bucketIndex = log.StartTime.Value.Hour / 2;

                if (bucketIndex < 0 || bucketIndex >= result.Count)
                {
                    continue;
                }

                result[bucketIndex].value += GetProcessedValue(log);
            }

            return result;
        }
        /// <summary>
        /// Dashboard 统计值规则：
        /// ProcessedCount > 0 时优先使用 ProcessedCount；
        /// 否则使用 SuccessCount + FailureCount。
        /// </summary>
        private static int GetProcessedValue(DashboardTaskLogDto log)
        {
            if (log == null)
            {
                return 0;
            }

            if (log.ProcessedCount.HasValue && log.ProcessedCount.Value > 0)
            {
                return log.ProcessedCount.Value;
            }

            int successCount = log.SuccessCount.HasValue ? log.SuccessCount.Value : 0;
            int failureCount = log.FailureCount.HasValue ? log.FailureCount.Value : 0;

            int value = successCount + failureCount;

            return value < 0 ? 0 : value;
        }

        /// <summary>
        /// 读取并限制 activities 的 limit 参数。
        /// 默认 20，最小 1，最大 100。
        /// </summary>
        private int ReadLimit()
        {
            string rawLimit = this.GetParam("limit");

            int limit;
            if (!int.TryParse(rawLimit, out limit))
            {
                limit = 20;
            }

            if (limit < 1)
            {
                return 1;
            }

            if (limit > 100)
            {
                return 100;
            }

            return limit;
        }

        /// <summary>
        /// 构造活动日志文本。
        /// 优先使用 Message，否则使用任务和状态兜底。
        /// </summary>
        private static string BuildActivityText(DashboardTaskLogDto log)
        {
            if (log == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(log.Message))
            {
                return log.Message;
            }

            string taskId = string.IsNullOrWhiteSpace(log.TaskId) ? "-" : log.TaskId;
            string status = string.IsNullOrWhiteSpace(log.Status) ? "-" : log.Status;

            return "任务 " + taskId + " 状态 " + status;
        }

        /// <summary>
        /// 将任务状态映射为前端展示级别。
        /// </summary>
        private static string MapActivityLevel(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "info";
            }

            string normalizedStatus = status.Trim();

            if (string.Equals(normalizedStatus, "Success", StringComparison.OrdinalIgnoreCase))
            {
                return "success";
            }

            if (string.Equals(normalizedStatus, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStatus, "Failure", StringComparison.OrdinalIgnoreCase))
            {
                return "error";
            }

            if (string.Equals(normalizedStatus, "PartialSuccess", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStatus, "NoData", StringComparison.OrdinalIgnoreCase))
            {
                return "warning";
            }

            return "info";
        }
    }
}
