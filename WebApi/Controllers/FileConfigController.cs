using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Common.Extensions;
using Nancy;
using Nancy.ModelBinding;
using System.Collections.Generic;
using System.Linq;


namespace Learun.Application.WebApi.Modules
{
    public class FileConfigController : BaseApi
    {
        private readonly IFileConfigService _fileConfigService;

        public FileConfigController(IFileConfigService fileConfigService) : base("/api/file-configs")
        {
            _fileConfigService = fileConfigService;

            // --- 核心配置 (Config) 路由映射 ---
            Get["/"] = GetConfigList;
            Get["/{id:int}"] = GetConfigEntity;
            Post["/"] = CreateConfig;
            Put["/{id:int}"] = UpdateConfig;
            Delete["/"] = DeleteConfigs;

            // 状态管理
            Get["/status"] = GetConfigStatus;
            Patch["/status"] = SetConfigStatus;
        }

        #region 1. 基础 CRUD 接口
        /// <summary>
        /// 获取配置列表
        /// </summary>
        /// <summary>
        /// 获取配置（支持分页或全量）
        /// </summary>
        private Response GetConfigList(dynamic _)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this);

            // 1. 统一构建查询条件
            var options = new FileConfigQueryOptions
            {
                TableName = ctx.TableName,
                DatabaseName = ctx.DatabaseName,
                TaskIds = this.GetQueryArray("taskIds"),
                GroupIds = this.GetQueryArray("groupIds"),
                Ids = this.GetQueryArray("ids")
            };

            // 2. 检查是否显式要求“全量数据”
            // 比如前端传了 ?all=true 或者判断 Query 字典里是否包含 limit 键
            bool isAll = this.Request.Query["all"].HasValue && (bool)this.Request.Query["all"];

            if (isAll)
            {
                var data = _fileConfigService.GetFileConfigs(options);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", data);
            }

            // 3. 默认走分页逻辑（使用你提供的扩展方法获取默认值）
            int page = this.GetPage();
            int limit = this.GetLimit();

            var pagedData = _fileConfigService.GetFileConfigsPaged(options, page, limit);

            return this.ToPageResponse(
                NancyModuleExtensions.ResponseCode.success,
                "查询成功",
                pagedData.Total,
                pagedData.List
            );
        }

        /// <summary>
        /// 获取单个配置详情
        /// </summary>
        private Response GetConfigEntity(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this);
            string id = (string)p.id;

            if (string.IsNullOrEmpty(id))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：ID不能为空", null);

            IEnumerable<AcquisitionConfig> data = _fileConfigService.GetByIds(new[] { id }, ctx.TableName, ctx.DatabaseName);
            var entity = data?.FirstOrDefault();

            return entity != null
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", entity)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "未找到该配置信息", null);
        }

        /// <summary>
        /// 新增配置
        /// </summary>
        private Response CreateConfig(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this);
            var config = this.Bind<AcquisitionConfig>();

            if (config == null)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败", null);

            int newId = _fileConfigService.CreateConfig(config, ctx.TableName, ctx.DatabaseName);
            return newId > 0
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "新增成功", newId)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "新增失败", null);
        }

        /// <summary>
        /// 更新配置
        /// </summary>
        private Response UpdateConfig(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this);
            var config = this.Bind<AcquisitionConfig>();

            if (config == null)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败", null);

            config.Id = (int)p.id;

            bool success = _fileConfigService.UpdateConfig(config, ctx.TableName, ctx.DatabaseName);
            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "更新成功", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "更新失败", null);
        }

        /// <summary>
        /// 批量删除配置
        /// </summary>
        private Response DeleteConfigs(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this);
            string[] ids = this.GetQueryArray("ids");

            if (ids == null || ids.Length == 0)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少待删除的 ids 参数", null);

            bool success = _fileConfigService.DeleteConfigs(ids, ctx.TableName, ctx.DatabaseName);
            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, $"成功删除 {ids.Length} 条记录", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "删除失败", null);
        }

        #endregion

        #region 2. 状态管理接口

        /// <summary>
        /// 获取指定配置列表的状态
        /// </summary>
        private Response GetConfigStatus(dynamic p)
        {
            string[] ids = this.GetQueryArray("ids");
            if (ids == null)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少参数 ids", null);

            var ctx = NancyModuleExtensions.GetQueryContext(this);
            var data = _fileConfigService.GetStatusByIds(ids, "EqName", ctx.TableName, ctx.DatabaseName);

            return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", data);
        }

        /// <summary>
        /// 批量设置配置启用/禁用
        /// </summary>
        private Response SetConfigStatus(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this);
            string[] ids = this.GetQueryArray("ids");

            if (ids == null)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：未指定ID", null);

            bool isEnabled = this.GetBool("isEnabled");

            bool success = _fileConfigService.SetEnabledStatus(ids, isEnabled, ctx.TableName, ctx.DatabaseName);
            return success
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "操作成功", null)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "操作失败", null);
        }

        #endregion
    }
}