using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Common.Extensions;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using Nancy;
using Nancy.ModelBinding;
using System;
using System.Linq;

namespace Learun.Application.WebApi.Modules.DT_DataAcquisitionSystem.WebApi.Controllers
{
    public class AcquisitionTaskController : BaseApi
    {
        private readonly IAcquisitionTaskService _taskService;
        private readonly IDataService _dataService;

        public AcquisitionTaskController(IAcquisitionTaskService taskService, IDataService dataService)
            : base("/api/data-acquisition/tasks")
        {
            _taskService = taskService;
            _dataService = dataService;

            // --- 任务 (Task) 路由映射 ---

            // 1. 列表查询
            Get["/"] = GetTaskList;

            // 2. 详情查询
            Get["/{id}"] = GetTaskEntity;

            // 3. 按模式查询 (如定时/循环)
            Get["/mode/{mode:int}"] = GetTasksByMode;

            // 4. 新增
            Post["/"] = CreateTask;

            // 5. 更新
            Put["/{id:int}"] = UpdateTask;

            // 6. 批量删除 (QueryString: ?ids=1,2)
            Delete["/"] = DeleteTasks;

            // 7. 批量切换启用状态 (QueryString: ?ids=1,2&isEnabled=true)
            Patch["/status"] = SetEnabledStatus;

            // --- 任务关联 (Task-Group Link) 路由映射 ---

            // 8. 获取任务已关联的组 ID 列表
            Get["/{taskId:int}/groups"] = GetAssociatedGroupIds;

            // 9. 为任务分配组 (全量更新)
            Post["/{taskId:int}/groups"] = AssignGroupsToTask;
        }

        #region 任务 CRUD 接口实现

        private Response GetTaskList(dynamic _)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionTask");

            var data = _taskService.GetList(ctx.TableName, ctx.DatabaseName);

            return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", data);
        }

        private Response GetTaskEntity(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionTask");
            string id = (string)p.id;

            if (string.IsNullOrEmpty(id))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误", null);

            var entity = _taskService.GetById(id, ctx.TableName, ctx.DatabaseName);

            return entity != null
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", entity)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "未找到任务数据", null);
        }

        private Response GetTasksByMode(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionTask");
            int mode = (int)p.mode;

            var data = _taskService.GetByMode(mode, ctx.TableName, ctx.DatabaseName);

            return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", data);
        }

        private Response CreateTask(dynamic _)
        {
            try
            {
                var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionTask");
                var task = this.Bind<AcquisitionTask>();

                if (task == null)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败", null);

                int newId = _taskService.CreateTask(task, ctx.TableName, ctx.DatabaseName);

                return newId > 0
                    ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "任务创建成功", newId)
                    : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "任务创建失败", null);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"任务创建失败：{ex.Message}", null);
            }
        }

        private Response UpdateTask(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionTask");
            var task = this.Bind<AcquisitionTask>();

            if (task == null)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败", null);

            if (p.id != null)
                task.Id = (int)p.id;

            bool success = _taskService.UpdateTask(task, ctx.TableName, ctx.DatabaseName);

            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "任务更新成功", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "任务更新失败", null);
        }

        private Response DeleteTasks(dynamic _)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionTask");
            string[] ids = this.GetQueryArray("ids");

            if (ids == null || ids.Length == 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少待删除的任务 ids", null);

            bool success = _taskService.DeleteTasks(ids, ctx.TableName, ctx.DatabaseName);

            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, $"成功删除 {ids.Length} 个任务", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "任务删除失败", null);
        }

        private Response SetEnabledStatus(dynamic _)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionTask");
            string[] ids = this.GetQueryArray("ids");
            bool isEnabled = this.GetBool("isEnabled");

            if (ids == null || ids.Length == 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少参数 ids", null);

            bool success = _taskService.SetEnabledStatus(ids, isEnabled, ctx.TableName, ctx.DatabaseName);

            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "任务状态更新成功", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "任务状态更新失败", null);
        }

        #endregion

        #region 关联操作 (Task - Group)

        /// <summary>
        /// 获取当前任务已关联的配置组 ID
        /// </summary>
        private Response GetAssociatedGroupIds(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionTask_Group");
            int taskId = (int)p.taskId;

            var groupIds = _taskService.GetAssociatedGroupIds(taskId, ctx.TableName, ctx.DatabaseName);

            return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", groupIds);
        }

        /// <summary>
        /// 批量为任务分配配置组 (全量保存)
        /// URL: POST /api/data-acquisition/tasks/{taskId}/groups?ids=1,2,3
        /// </summary>
        private Response AssignGroupsToTask(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionTask_Group");
            int taskId = (int)p.taskId;

            string[] idsStr = this.GetQueryArray("ids");
            int[] groupIds = idsStr?.Select(int.Parse).ToArray() ?? new int[0];

            bool success = _taskService.AssignGroupsToTask(taskId, groupIds, ctx.TableName, ctx.DatabaseName);

            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "组关联分配成功", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "组关联分配失败", null);
        }

        #endregion
    }
}
