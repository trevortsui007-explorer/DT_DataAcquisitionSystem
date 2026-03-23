using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Common.Extensions;
using DT_DataAcquisitionSystem.Application.Services;
using Nancy;
using Nancy.ModelBinding;
using System;
using System.Threading.Tasks;
using Learun.Application.WebApi;
using System.Threading;


namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    /// <summary>
    /// 采集状态与进度管理控制器
    /// </summary>
    public class AcquisitionStateController : BaseApi
    {
        private readonly IAcquisitionLogService _logService;

        public AcquisitionStateController(IAcquisitionLogService logService) : base("/api/data-acquisition")
        {
            _logService = logService;

            // --- 路由映射 ---

            // 获取指定配置的起始行号 (断点续传)
            // 示例: GET /api/data-acquisition/next-row/101
            Get["/next-row/{configId:int}", true] = async (p, ct) => await GetNextStartRow(p, ct);

            // 记录采集【明细】日志
            // 示例: POST /api/data-acquisition/log
            Post["/log", true] = async (p, ct) => await RecordLogEntry(p, ct);

            // 记录采集【总任务】日志
            // 示例: POST /api/data-acquisition/task-log
            Post["/task-log", true] = async (p, ct) => await RecordTaskLogEntry(p, ct);

            // 更新任务状态
            // 示例: PUT /api/data-acquisition/task-log/a1b2c3d4...
            Put["/task-log/{id}", true] = async (p, ct) => await UpdateTask(p, ct);
        }

        /// <summary>
        /// 获取下一次采集的起始行号
        /// 逻辑：优先查日志表的断点，若无则查配置表的初始值
        /// </summary>
        private async Task<Response> GetNextStartRow(dynamic p, CancellationToken ct)
        {
            int configId = (int)p.configId;
            string fileName = this.GetParam("fileName");

            if (configId <= 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：ConfigId 必须大于 0", null);

            if (string.IsNullOrWhiteSpace(fileName))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "请输入要查询的文件名", null);

            try
            {
                int nextRow = await _logService.GetNextStartRowAsync(configId, fileName, ct);

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", new
                {
                    ConfigId = configId,
                    NextStartRow = nextRow
                });
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.exception, $"获取起始行失败: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 记录本次采集进度到明细日志表 (DA_AcquisitionLog)
        /// </summary>
        private async Task<Response> RecordLogEntry(dynamic _, CancellationToken ct)
        {
            try
            {
                // 使用 Nancy 内置的 ModelBinding 绑定请求体到实体
                var entry = this.Bind<AcquisitionLogEntry>();

                if (entry == null)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败：请求体为空或格式错误", null);

                // 调用 Service 进行业务校验与入库，并获取生成的 ID
                string newId = await _logService.RecordLogEntryAsync(entry, ct);

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "明细进度记录成功", new { Id = newId });
            }
            catch (InvalidOperationException ex)
            {
                // 捕获 Service 层抛出的业务校验异常
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, ex.Message, null);
            }
            catch (Exception ex)
            {
                // 捕获系统级异常
                return this.ToResponse(NancyModuleExtensions.ResponseCode.exception, $"记录明细进度异常: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 记录采集总任务到任务日志表 (DA_AcquisitionTaskLog)
        /// </summary>
        private async Task<Response> RecordTaskLogEntry(dynamic _, CancellationToken ct)
        {
            try
            {
                var entry = this.Bind<AcquisitionTaskLogEntry>();

                if (entry == null)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败：请求体为空或格式错误", null);

                // 调用 Service 进行业务校验与入库，并获取生成的 ID
                string newId = await _logService.RecordTaskLogEntryAsync(entry, ct);

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "任务日志记录成功", new { Id = newId });
            }
            catch (InvalidOperationException ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, ex.Message, null);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.exception, $"记录任务日志异常: {ex.Message}", null);
            }
        }

        private async Task<Response> UpdateTask(dynamic p, CancellationToken ct)
        {
            string id = (string)p.id;
            // 从请求体获取更新内容 (Status, SuccessCount)
            var req = this.Bind<AcquisitionTaskLogEntry>();

            try
            {
                bool success = await _logService.UpdateTaskStatusAsync(id, req.Status, req.SuccessCount, ct);

                if (success)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "任务状态更新成功", null);
                else
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "更新失败，未找到对应记录", null);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.exception, $"更新异常: {ex.Message}", null);
            }
        }
    }
}