using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Common.Extensions;
using Nancy;
using Nancy.ModelBinding;
using System;
using System.Linq;
using System.Threading.Tasks;
using Learun.Application.WebApi;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    /// <summary>
    /// 文件自动化采集任务执行接口
    /// </summary>
    public class AcquisitionController : BaseApi
    {
        private readonly DataAcquisitionService _acquisitionService;
        private readonly IFileConfigService _fileConfigService;
        private readonly IAcquisitionLogService _logService;

        public AcquisitionController(
            DataAcquisitionService acquisitionService,
            IFileConfigService fileConfigService, IAcquisitionLogService logService) : base("/api/data-acquisition")
        {
            _acquisitionService = acquisitionService;
            _fileConfigService = fileConfigService;
            _logService = logService;

            // --- 任务触发路由映射 ---

            // 1. 根据配置 ID 触发采集 (POST) - 支持从 URL 或 Query 传参
            Post["/execute-by-id/{id:int}", true] = async (p, ct) => await ExecuteById(p, ct);

            // 2. 补录/手动触发接口 (POST) - 接收完整配置对象
            Post["/execute-manual/{id:int}", true] = async (p, ct) => await ExecuteManual(p, ct);

            // 3. 根据任务 IDs 批量执行 (POST)
            Post["/execute-by-tasks", true] = async (p, ct) => await ExecuteByTasks(p, ct);

            // 4. 根据组 IDs 批量执行 (POST)
            Post["/execute-by-groups", true] = async (p, ct) => await ExecuteByGroups(p, ct);

            // 5. 根据配置 IDs 批量执行 (POST)
            Post["/execute-by-ids", true] = async (p, ct) => await ExecuteByIds(p, ct);

            // 6. 按时间范围补录单个配置 (POST)
            Post["/execute-by-range/{id}", true] = async (p, ct) => await ExecuteByTimeRange(p, ct);

            // 7. 按时间范围补录多个配置 (POST)
            Post["/execute-configs-range", true] = async (p, ct) => await ExecuteConfigsByTimeRange(p, ct);
        }

        #region 1. 任务触发接口实现

        /// <summary>
        /// 根据配置 ID 触发采集任务
        /// </summary>
        private async Task<Response> ExecuteById(dynamic p, System.Threading.CancellationToken ct)
        {
            // 1. 获取统一上下文（TableName/DatabaseName）
            var ctx = NancyModuleExtensions.GetQueryContext(this);

            // 2. 解析参数
            string id = (string)p.id;
            // 尝试获取指定的处理日期，如果不传则默认当天
            DateTime processDate = DateTime.TryParse(this.GetParam("processDate"), out var dt) ? dt : DateTime.Now;

            if (string.IsNullOrEmpty(id))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：ID 不能为空", null);

            try
            {
                // 3. 获取配置实体（复用 FileConfigController 里的逻辑）
                var configs = _fileConfigService.GetByIds(new[] { id }, ctx.TableName, ctx.DatabaseName);
                var config = configs?.FirstOrDefault();

                if (config == null)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"未找到 ID 为 {id} 的配置信息", null);

                // 4. 调用 Service 执行核心逻辑
                var result = await _acquisitionService.ProcessSingleConfig(config, processDate, ct);

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "采集任务已触发并完成", result);
            }
            catch (Exception ex)
            {
                // 记录日志建议在 Service 层完成，此处返回友好错误
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"任务执行异常: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 补录/手动执行
        /// </summary>
        private async Task<Response> ExecuteManual(dynamic p, System.Threading.CancellationToken ct)
        {
            // 1. 获取统一上下文（TableName/DatabaseName）
            var ctx = NancyModuleExtensions.GetQueryContext(this);

            // 2. 解析参数
            string id = (string)p.id;
            // 尝试获取指定的处理日期，如果不传则默认当天
            DateTime processDate = DateTime.TryParse(this.GetParam("processDate"), out var dt) ? dt : DateTime.Now;

            if (string.IsNullOrEmpty(id))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：ID 不能为空", null);

            // 采集开始：记录手动Task日志
            var taskLogEntry = new AcquisitionTaskLogEntry
            {
                TaskId = 0,
                StartTime = DateTime.Now,
                Status = "Running",
                TotalConfigs = 1
            };
            string taskLogId = await _logService.RecordTaskLogEntryAsync(taskLogEntry, ct);

            try
            {
                // 3. 获取配置实体（复用 FileConfigController 里的逻辑）
                var configs = _fileConfigService.GetByIds(new[] { id }, ctx.TableName, ctx.DatabaseName);
                var config = configs?.FirstOrDefault();

                if (config == null)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"未找到 ID 为 {id} 的配置信息", null);

                // 4. 调用 Service 执行核心逻辑
                await _acquisitionService.ProcessSingleConfig(config, processDate, taskLogId, ct);

                // 采集成功：更新Task状态
                await _logService.UpdateTaskStatusAsync(taskLogId, "Success", 1);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "采集任务已触发并完成", null);
            }
            catch (Exception ex)
            {
                // 采集失败：更新Task状态
                await _logService.UpdateTaskStatusAsync(taskLogId, "Failed", 0);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"任务执行异常: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 根据任务 IDs 批量触发采集任务
        /// </summary>
        private async Task<Response> ExecuteByTasks(dynamic p, System.Threading.CancellationToken ct)
        {
            // 获取日期和 ID 数组
            DateTime processDate = DateTime.TryParse(this.GetParam("processDate"), out var dt) ? dt : DateTime.Now;
            string[] taskIds = this.GetQueryArray("taskIds");

            if (taskIds == null || taskIds.Length == 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：taskIds 不能为空", null);

            try
            {
                var summary = await _acquisitionService.ProcessByTask(taskIds, processDate, ct);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "按任务采集已执行", summary);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"批量任务执行异常: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 根据组 IDs 批量触发采集任务
        /// </summary>
        private async Task<Response> ExecuteByGroups(dynamic p, System.Threading.CancellationToken ct)
        {
            // 获取日期和 ID 数组
            DateTime processDate = DateTime.TryParse(this.GetParam("processDate"), out var dt) ? dt : DateTime.Now;
            string[] groupIds = this.GetQueryArray("groupIds");

            if (groupIds == null || groupIds.Length == 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：groupIds 不能为空", null);

            try
            {
                var summary = await _acquisitionService.ProcessByGroup(groupIds, processDate, ct);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "按组采集已执行", summary);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"批量组执行异常: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 根据配置 IDs 批量触发采集任务
        /// </summary>
        private async Task<Response> ExecuteByIds(dynamic p, System.Threading.CancellationToken ct)
        {
            DateTime processDate = DateTime.TryParse(this.GetParam("processDate"), out var dt) ? dt : DateTime.Now;
            string[] ids = this.GetQueryArray("ids");

            if (ids == null || ids.Length == 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：ids 不能为空", null);

            try
            {
                var summary = await _acquisitionService.ProcessByIds(ids, processDate, ct);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "按配置 ID 批量采集已执行", summary);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"批量配置执行异常: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 根据时间范围补录单个配置
        /// </summary>
        private async Task<Response> ExecuteByTimeRange(dynamic p, System.Threading.CancellationToken ct)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this);
            string id = (string)p.id;

            // 解析开始和结束时间
            if (!DateTime.TryParse(this.GetParam("startDate"), out DateTime start))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：开始日期 startDate 无效", null);

            if (!DateTime.TryParse(this.GetParam("endDate"), out DateTime end))
                end = DateTime.Now; // 如果没传结束日期，默认到今天

            try
            {
                // 1. 先查出该配置实体
                var configs = _fileConfigService.GetByIds(new[] { id }, ctx.TableName, ctx.DatabaseName);
                var config = configs?.FirstOrDefault();

                if (config == null)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"未找到 ID 为 {id} 的配置", null);

                // 2. 调用 Service 进行范围补录
                var summary = await _acquisitionService.ProcessByTimeRange(config, start, end, ct);

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success,
                    $"已启动补录任务：从 {start:yyyy-MM-dd} 到 {end:yyyy-MM-dd}", summary);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"补录任务执行异常: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 按时间范围补录多个配置的数据 (支持 TaskIds, GroupIds, Ids 混合查询)
        /// </summary>
        private async Task<Response> ExecuteConfigsByTimeRange(dynamic p, System.Threading.CancellationToken ct)
        {
            // 1. 获取库/表上下文
            var ctx = NancyModuleExtensions.GetQueryContext(this);

            // 2. 构造查询参数对象
            var options = new FileConfigQueryOptions
            {
                TableName = ctx.TableName,
                DatabaseName = ctx.DatabaseName,
                TaskIds = this.GetQueryArray("taskIds"),
                GroupIds = this.GetQueryArray("groupIds"),
                Ids = this.GetQueryArray("ids")
            };

            // 3. 解析时间范围
            if (!DateTime.TryParse(this.GetParam("startDate"), out DateTime start))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：开始日期 startDate 格式不正确", null);

            if (!DateTime.TryParse(this.GetParam("endDate"), out DateTime end))
                end = DateTime.Now; // 默认补录到今天

            if (start > end)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：开始日期不能晚于结束日期", null);

            try
            {
                // 4. 调用 Service 执行批量录入
                var summary = await _acquisitionService.ProcessConfigsByTimeRange(options, start, end, ct);

                string msg = $"补录任务已启动: {start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}";
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, msg, summary);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"多配置批量补录异常: {ex.Message}", null);
            }
        }

        #endregion
    }
}