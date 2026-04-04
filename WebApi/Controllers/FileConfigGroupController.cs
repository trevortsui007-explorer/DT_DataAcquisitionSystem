using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Common.Extensions;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using Nancy;
using Nancy.ModelBinding;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using DT_DataAcquisitionSystem.Application.DTOs;

namespace Learun.Application.WebApi.Modules.DT_DataAcquisitionSystem.WebApi.Controllers
{
    public class FileConfigGroupController : BaseApi
    {
        private readonly IFileConfigGroupService _fileConfigGroupService;
        private readonly IDataService _dataService;

        public FileConfigGroupController(IFileConfigGroupService fileConfigGroupService, IDataService dataService) : base("/api/file-configs")
        {
            _fileConfigGroupService = fileConfigGroupService;
            _dataService = dataService;

            // --- 配置组 (Group) 路由映射 ---

            // 1. 列表查询
            Get["/group/"] = GetConfigGroupList;

            // 2. 详情查询
            Get["/group/{id}"] = GetConfigGroupEntity;

            // 3. 新增
            Post["/group/"] = CreateConfigGroup;

            // 4. 更新
            Put["/group/{id:int}"] = UpdateConfigGroup;

            // 5. 批量删除 (QueryString: ?ids=1,2)
            Delete["/group/"] = DeleteConfigGroups;

            // 6. 快捷切换状态 (QueryString: ?ids=1,2&isEnabled=true)
            Patch["/group/status"] = SetEnabledStatus;

            // 7. 快捷切换状态
            Get["/group/status"] = GetConfigGroupStatus;

            // 7. 快捷切换状态
            Get["/group/status"] = GetConfigGroupStatus;

            // --- 配置组关联 (Config-Group Link) 路由映射 ---

            // 8. 批量建立关联
            Post["/group/{groupId:int}/configs"] = AddConfigsToGroup;

            // 9. 批量解除关联
            Delete["/group/{groupId:int}/configs"] = RemoveConfigsFromGroup;
        }

        #region 接口实现

        private Response GetConfigGroupList(dynamic _)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionGroup");
            var linkTableName = this.GetParam("LinkTableName");
            var data = _fileConfigGroupService.GetList(ctx.TableName, linkTableName, ctx.DatabaseName);
            return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", data);
        }

        private Response GetConfigGroupEntity(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionGroup");
            string id = (string)p.id;

            if (string.IsNullOrEmpty(id))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误", null);

            IEnumerable<AcquisitionGroup> data = _fileConfigGroupService.GetByIds(new[] { id }, ctx.TableName, ctx.DatabaseName);
            var entity = data?.FirstOrDefault();

            return entity != null
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", entity)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "未找到数据", null);
        }

        private Response CreateConfigGroup(dynamic _)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionGroup");
            var group = this.Bind<AcquisitionGroup>();

            if (group == null) return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败", null);

            int newId = _fileConfigGroupService.CreateConfigGroup(group, ctx.TableName, ctx.DatabaseName);
            return newId > 0
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "新增成功", newId)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "新增失败", null);
        }

        private Response UpdateConfigGroup(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionGroup");
            var group = this.Bind<AcquisitionGroup>();

            if (group == null) return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败", null);

            // 路由 ID 优先级最高
            if (p.id != null) group.Id = (int)p.id;

            bool success = _fileConfigGroupService.UpdateConfig(group, ctx.TableName, ctx.DatabaseName);
            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "更新成功", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "更新失败", null);
        }

        private Response DeleteConfigGroups(dynamic _)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionGroup");
            string[] ids = this.GetQueryArray("ids");

            if (ids == null || ids.Length == 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少待删除的 ids", null);

            bool success = _fileConfigGroupService.DeleteConfigs(ids, ctx.TableName, ctx.DatabaseName);
            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, $"成功删除 {ids.Length} 条记录", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "删除失败", null);
        }

        /// <summary>
        /// 获取组状态
        /// </summary>
        private Response GetConfigGroupStatus(dynamic p)
        {
            string[] ids = this.GetQueryArray("ids");
            if (ids == null) return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少参数 ids", null);

            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionGroup");
            // 组通常显示 GroupName
            var data = _fileConfigGroupService.GetStatusByIds(ids, "GroupName", ctx.TableName, ctx.DatabaseName);

            return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", data);
        }

        /// <summary>
        /// 批量启用/禁用状态切换
        /// </summary>
        private Response SetEnabledStatus(dynamic _)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionGroup");
            string[] ids = this.GetQueryArray("ids");
            bool isEnabled = this.GetBool("isEnabled");

            if (ids == null || ids.Length == 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少参数 ids", null);

            bool success = _fileConfigGroupService.SetEnabledStatus(ids, isEnabled, ctx.TableName, ctx.DatabaseName);
            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "状态更新成功", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "状态更新失败", null);
        }

        #endregion

        #region 关联操作（配置 - 配置组）

        /// <summary>
        /// 批量建立关联关系
        /// </summary>
        public class ConfigRequest
        {
            public int[] ConfigIds { get; set; }
        }

        private Response AddConfigsToGroup(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionGroup_Config");
            int groupId = (int)p.groupId;

            // 2. 从 QueryString 获取 ids，例如: /group/1/configs?ids=101,102
            string[] idsStr = this.GetQueryArray("ids");
            var configIds = idsStr?.Select(int.Parse).ToArray();

            if (configIds == null || configIds.Length == 0)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少待关联的配置 ID 列表，请检查 JSON Key 是否为 'configIds'", null);
            }

            bool success = _fileConfigGroupService.AddConfigsToGroup(groupId, configIds, ctx.TableName, ctx.DatabaseName);

            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "关联成功", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "关联失败", null);
        }

        /// <summary>
        /// 批量解除关联关系
        /// </summary>
        private Response RemoveConfigsFromGroup(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_AcquisitionGroup_Config");
            int groupId = (int)p.groupId;

            // 从 QueryString 获取 ids，例如: /group/1/configs?ids=101,102
            string[] idsStr = this.GetQueryArray("ids");
            var configIds = idsStr?.Select(int.Parse).ToArray();

            if (configIds == null || configIds.Length == 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少待解除的配置 ID 列表", null);

            bool success = _fileConfigGroupService.RemoveConfigsFromGroup(groupId, configIds, ctx.TableName, ctx.DatabaseName);

            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "解除关联成功", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "解除关联失败", null);
        }

        #endregion
    }
}