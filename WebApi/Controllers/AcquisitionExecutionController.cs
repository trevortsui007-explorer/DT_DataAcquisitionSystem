using System;
using System.Configuration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Common.Extensions;
using Learun.Application.WebApi;
using Nancy;
using Nancy.ModelBinding;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    /// <summary>
    /// 采集执行器任务启动与状态查询接口
    /// </summary>
    public class AcquisitionExecutionController : BaseApi
    {
        private readonly IAcquisitionExecutionService _executionService;
        private readonly IFileConfigService _fileConfigService;

        public AcquisitionExecutionController(
            IAcquisitionExecutionService executionService,
            IFileConfigService fileConfigService)
            : base("/api/data-acquisition/execution")
        {
            _executionService = executionService;
            _fileConfigService = fileConfigService;

            // --- 启动接口 ---

            // 1. 根据配置 IDs 启动
            Post["/start/by-ids", true] = async (p, ct) => await StartByIds(p, ct);

            // 2. 根据组 IDs 启动
            Post["/start/by-groups", true] = async (p, ct) => await StartByGroups(p, ct);

            // 3. 根据任务 IDs 启动
            Post["/start/by-tasks", true] = async (p, ct) => await StartByTasks(p, ct);

            // 3.1 SQL Server Agent 专用：按任务 IDs 以定时触发类型启动
            Post["/start/scheduled/by-tasks", true] = async (p, ct) => await StartScheduledByTasks(p, ct);

            // 4. 根据单个配置 ID + 时间范围启动
            Post["/start/by-range/{id}", true] = async (p, ct) => await StartByRange(p, ct);

            // 5. 根据配置查询条件 + 时间范围启动
            Post["/start/configs-range", true] = async (p, ct) => await StartConfigsRange(p, ct);

            // 6. 取消运行中的采集任务
            Post["/{taskLogId}/cancel", true] = async (p, ct) => await CancelTask(p, ct);

            // --- 查询接口 ---

            // 7. 查询任务状态
            Get["/{taskLogId}/status", true] = async (p, ct) => await GetTaskStatus(p, ct);

            // 8. 查询任务明细统计
            Get["/{taskLogId}/details/summary", true] = async (p, ct) => await GetTaskDetailSummary(p, ct);

            // 9. 查询任务明细
            Get["/{taskLogId}/details", true] = async (p, ct) => await GetTaskDetails(p, ct);

            // 10. 查询任务列表
            Get["/task-logs", true] = async (p, ct) => await GetTaskLogs(p, ct);

            // 11. 批量查询历史任务文件缺失 Warning 汇总
            Post["/task-logs/warning-summary", true] = async (p, ct) => await GetTaskLogWarningSummary(p, ct);
        }

        #region 启动接口

        /// <summary>
        /// POST /start/by-ids
        /// </summary>
        private async Task<Response> StartByIds(dynamic p, CancellationToken ct)
        {
            DateTime processDate = DateTime.TryParse(this.GetParam("processDate"), out var dt)
                ? dt
                : DateTime.Now;

            string[] ids = this.GetQueryArray("ids");

            if (ids == null || ids.Length == 0)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：ids 不能为空",
                    null);
            }

            try
            {
                var result = await _executionService
                    .StartByIdsAsync(ids, processDate, ct)
                    .ConfigureAwait(false);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    result?.Message ?? "采集任务已启动",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"按配置启动任务异常: {ex.Message}",
                    null);
            }
        }

        /// <summary>
        /// POST /start/by-groups
        /// </summary>
        private async Task<Response> StartByGroups(dynamic p, CancellationToken ct)
        {
            DateTime processDate = DateTime.TryParse(this.GetParam("processDate"), out var dt)
                ? dt
                : DateTime.Now;

            string[] groupIds = this.GetQueryArray("groupIds");

            if (groupIds == null || groupIds.Length == 0)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：groupIds 不能为空",
                    null);
            }

            try
            {
                var result = await _executionService
                    .StartByGroupsAsync(groupIds, processDate, ct)
                    .ConfigureAwait(false);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    result?.Message ?? "分组采集任务已启动",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"按组启动任务异常: {ex.Message}",
                    null);
            }
        }

        /// <summary>
        /// POST /start/by-tasks
        /// </summary>
        private async Task<Response> StartByTasks(dynamic p, CancellationToken ct)
        {
            DateTime processDate = DateTime.TryParse(this.GetParam("processDate"), out var dt)
                ? dt
                : DateTime.Now;

            string[] taskIds = this.GetQueryArray("taskIds");

            if (taskIds == null || taskIds.Length == 0)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：taskIds 不能为空",
                    null);
            }

            try
            {
                var result = await _executionService
                    .StartByTasksAsync(taskIds, processDate, ct)
                    .ConfigureAwait(false);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    result?.Message ?? "计划任务采集已启动",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"按任务启动异常: {ex.Message}",
                    null);
            }
        }

        /// <summary>
        /// POST /start/scheduled/by-tasks
        /// SQL Server Agent 专用入口，会生成 SCH 任务编号。
        /// </summary>
        private async Task<Response> StartScheduledByTasks(dynamic p, CancellationToken ct)
        {
            if (!ValidateSchedulerKey(out string authError))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    authError,
                    null);
            }

            DateTime processDate = DateTime.TryParse(this.GetParam("processDate"), out var dt)
                ? dt
                : DateTime.Now;

            string[] taskIds = this.GetQueryArray("taskIds");

            if (taskIds == null || taskIds.Length == 0)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：taskIds 不能为空",
                    null);
            }

            try
            {
                var result = await _executionService
                    .StartScheduledByTasksAsync(taskIds, processDate, ct)
                    .ConfigureAwait(false);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    result?.Message ?? "计划任务采集已启动",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"定时任务启动异常: {ex.Message}",
                    null);
            }
        }

        /// <summary>
        /// POST /start/by-range/{id}
        /// </summary>
        private async Task<Response> StartByRange(dynamic p, CancellationToken ct)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this);

            string id = (string)p.id;

            if (string.IsNullOrWhiteSpace(id))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：id 不能为空",
                    null);
            }

            if (!DateTime.TryParse(this.GetParam("startDate"), out var startDate))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：startDate 格式不正确",
                    null);
            }

            if (!DateTime.TryParse(this.GetParam("endDate"), out var endDate))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：endDate 格式不正确",
                    null);
            }

            if (endDate.Date < startDate.Date)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：结束时间不能早于开始时间",
                    null);
            }

            try
            {
                var configs = _fileConfigService.GetByIds(new[] { id }, ctx.TableName, ctx.DatabaseName);
                var config = configs?.FirstOrDefault();

                if (config == null)
                {
                    return this.ToResponse(
                        NancyModuleExtensions.ResponseCode.fail,
                        $"未找到 ID 为 {id} 的配置信息",
                        null);
                }

                var result = await _executionService
                    .StartByRangeAsync(config, startDate, endDate, ct)
                    .ConfigureAwait(false);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    result?.Message ?? "区间采集任务已启动",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"按时间范围启动异常: {ex.Message}",
                    null);
            }
        }

        /// <summary>
        /// POST /start/configs-range
        /// </summary>
        private async Task<Response> StartConfigsRange(dynamic p, CancellationToken ct)
        {
            if (!DateTime.TryParse(this.GetParam("startDate"), out var startDate))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：startDate 格式不正确",
                    null);
            }

            if (!DateTime.TryParse(this.GetParam("endDate"), out var endDate))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：endDate 格式不正确",
                    null);
            }

            if (endDate.Date < startDate.Date)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：结束时间不能早于开始时间",
                    null);
            }

            try
            {
                var options = this.Bind<Domain.Entities.FileConfigQueryOptions>() ?? new Domain.Entities.FileConfigQueryOptions();

                var result = await _executionService
                    .StartConfigsByRangeAsync(options, startDate, endDate, ct)
                    .ConfigureAwait(false);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    result?.Message ?? "批量区间采集任务已启动",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"按条件批量区间启动异常: {ex.Message}",
                    null);
            }
        }

        #endregion

        private bool ValidateSchedulerKey(out string error)
        {
            error = null;

            string expectedKey = ConfigurationManager.AppSettings["DAS.SchedulerKey"];
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                error = "定时触发密钥未配置，拒绝执行。";
                return false;
            }

            string providedKey = this.Request.Headers["X-DAS-Scheduler-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(providedKey) ||
                !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
            {
                error = "定时触发密钥无效，拒绝执行。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// POST /{taskLogId}/cancel
        /// </summary>
        private async Task<Response> CancelTask(dynamic p, CancellationToken ct)
        {
            string taskLogId = (string)p.taskLogId;

            if (string.IsNullOrWhiteSpace(taskLogId))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：taskLogId 不能为空",
                    null);
            }

            try
            {
                var result = await _executionService
                    .CancelTaskAsync(taskLogId, ct)
                    .ConfigureAwait(false);

                bool success = result != null && string.Equals(result.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

                return this.ToResponse(
                    success ? NancyModuleExtensions.ResponseCode.success : NancyModuleExtensions.ResponseCode.fail,
                    result?.Message ?? "取消任务失败",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"取消任务异常: {ex.Message}",
                    null);
            }
        }
        #region 查询接口

        /// <summary>
        /// GET /{taskLogId}/status
        /// </summary>
        private async Task<Response> GetTaskStatus(dynamic p, CancellationToken ct)
        {
            string taskLogId = (string)p.taskLogId;

            if (string.IsNullOrWhiteSpace(taskLogId))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：taskLogId 不能为空",
                    null);
            }

            try
            {
                var result = await _executionService
                    .GetTaskStatusAsync(taskLogId, ct)
                    .ConfigureAwait(false);

                if (result == null)
                {
                    return this.ToResponse(
                        NancyModuleExtensions.ResponseCode.fail,
                        $"未找到 taskLogId = {taskLogId} 的任务状态",
                        null);
                }

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    "获取任务状态成功",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"获取任务状态异常: {ex.Message}",
                    null);
            }
        }

        /// <summary>
        /// GET /{taskLogId}/details
        /// </summary>
        private async Task<Response> GetTaskDetails(dynamic p, CancellationToken ct)
        {
            string taskLogId = (string)p.taskLogId;

            if (string.IsNullOrWhiteSpace(taskLogId))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：taskLogId 不能为空",
                    null);
            }

            try
            {
                string pageNoParam = this.GetParam("pageNo");
                string pageSizeParam = this.GetParam("pageSize");
                string status = this.GetParam("status");
                string errorCategory = this.GetParam("errorCategory");
                bool usePaging =
                    !string.IsNullOrWhiteSpace(pageNoParam) ||
                    !string.IsNullOrWhiteSpace(pageSizeParam) ||
                    !string.IsNullOrWhiteSpace(status) ||
                    !string.IsNullOrWhiteSpace(errorCategory);

                object result;

                if (usePaging)
                {
                    int pageNo = int.TryParse(pageNoParam, out var pn) ? pn : 1;
                    int pageSize = int.TryParse(pageSizeParam, out var ps) ? ps : 10;

                    if (pageNo <= 0) pageNo = 1;
                    if (pageSize <= 0) pageSize = 10;
                    if (pageSize > 200) pageSize = 200;

                    result = await _executionService
                        .GetTaskDetailsAsync(taskLogId, pageNo, pageSize, status, errorCategory, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    result = await _executionService
                        .GetTaskDetailsAsync(taskLogId, ct)
                        .ConfigureAwait(false);
                }

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    "获取任务明细成功",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"获取任务明细异常: {ex.Message}",
                    null);
            }
        }

        /// <summary>
        /// GET /{taskLogId}/details/summary
        /// </summary>
        private async Task<Response> GetTaskDetailSummary(dynamic p, CancellationToken ct)
        {
            string taskLogId = (string)p.taskLogId;

            if (string.IsNullOrWhiteSpace(taskLogId))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "参数错误：taskLogId 不能为空",
                    null);
            }

            try
            {
                var result = await _executionService
                    .GetTaskDetailSummaryAsync(taskLogId, ct)
                    .ConfigureAwait(false);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    "获取任务明细统计成功",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"获取任务明细统计异常: {ex.Message}",
                    null);
            }
        }

        /// <summary>
        /// GET /task-logs
        /// </summary>
        private async Task<Response> GetTaskLogs(dynamic p, CancellationToken ct)
        {
            int pageNo = int.TryParse(this.GetParam("pageNo"), out var pn) ? pn : 1;
            int pageSize = int.TryParse(this.GetParam("pageSize"), out var ps) ? ps : 20;

            string status = this.GetParam("status");
            int? taskId = int.TryParse(this.GetParam("taskId"), out var tid) ? tid : (int?)null;
            DateTime? startTime = DateTime.TryParse(this.GetParam("startTime"), out var st) ? st : (DateTime?)null;
            DateTime? endTime = DateTime.TryParse(this.GetParam("endTime"), out var et) ? et : (DateTime?)null;

            if (pageNo <= 0) pageNo = 1;
            if (pageSize <= 0) pageSize = 20;
            if (pageSize > 200) pageSize = 200;

            try
            {
                var result = await _executionService
                    .GetTaskLogsAsync(pageNo, pageSize, status, startTime, endTime, taskId, ct)
                    .ConfigureAwait(false);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    "获取任务日志列表成功",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"获取任务日志列表异常: {ex.Message}",
                    null);
            }
        }

        /// <summary>
        /// POST /task-logs/warning-summary
        /// </summary>
        private async Task<Response> GetTaskLogWarningSummary(dynamic p, CancellationToken ct)
        {
            try
            {
                var request = this.Bind<TaskLogWarningSummaryRequestDto>() ?? new TaskLogWarningSummaryRequestDto();
                var result = await _executionService
                    .GetTaskLogWarningSummaryAsync(request.TaskLogIds, ct)
                    .ConfigureAwait(false);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    "获取任务 Warning 汇总成功",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    $"获取任务 Warning 汇总异常: {ex.Message}",
                    null);
            }
        }

        #endregion
    }
}
