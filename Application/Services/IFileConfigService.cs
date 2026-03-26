using System.Collections.Generic;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Common.Extensions;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IFileConfigService
    {
        #region 查询操作 (Read)
        /// <summary>
        /// 根据组合条件（如机台名、表名、启用状态）获取文件采集配置分页列表
        /// </summary>
        NancyModuleExtensions.PageResult<AcquisitionConfig> GetFileConfigsPaged(FileConfigQueryOptions options, int page, int limit);

        /// <summary>
        /// 根据组合条件（如机台名、表名、启用状态）获取文件采集配置列表
        /// </summary>
        IEnumerable<AcquisitionConfig> GetFileConfigs(FileConfigQueryOptions options);

        /// <summary>
        /// 根据主键 ID 获取单个配置
        /// </summary>
        IEnumerable<AcquisitionConfig> GetByIds(string[] ids, string configTableName = null, string databaseName = null);

        /// <summary>
        /// 组查询：合并了单组和多组逻辑
        /// </summary>
        IEnumerable<AcquisitionConfig> GetConfigsByGroupIds(string[] groupIds, string configTableName = null, string configLinkTableName = null, string databaseName = null);

        /// <summary>
        /// 根据 Task ID 数组直接获取所有关联的 Configs
        /// </summary>
        IEnumerable<AcquisitionConfig> GetConfigsByTaskIds(string[] taskIds, string configTableName = null, string databaseName = null);

        /// <summary>
        /// 根据 ID 数组直接获取所有Configs的状态
        /// </summary>
        IEnumerable<AcquisitionConfigStatus> GetStatusByIds(string[] ids, string columnName = "EqName", string configTableName = null, string databaseName = null);

        #endregion

        #region 写入操作 (Create / Update / Delete)

        /// <summary>
        /// 新增采集配置项
        /// </summary>
        /// <returns>返回生成的 ID</returns>
        int CreateConfig(AcquisitionConfig config, string configTableName = null, string databaseName = null);

        /// <summary>
        /// 更新现有配置信息
        /// </summary>
        bool UpdateConfig(AcquisitionConfig config, string configTableName = null, string databaseName = null);

        /// <summary>
        /// 删除配置（通常建议物理删除前进行引用检查）
        /// </summary>
        bool DeleteConfigs(string[] ids, string configTableName = null, string databaseName = null);

        /// <summary>
        /// 快捷切换配置的启用/禁用状态
        /// </summary>
        bool SetEnabledStatus(string[] ids, bool isEnabled, string configTableName = null, string databaseName = null);

        #endregion
    }
}