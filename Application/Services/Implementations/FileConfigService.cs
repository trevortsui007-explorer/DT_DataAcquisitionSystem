using System;
using System.Collections.Generic;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Common.Extensions;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class FileConfigService : IFileConfigService
    {
        private const string DefaultDb = "BaseDb";
        private const string DefaultConfigTable = "DA_AcquisitionConfig";
        private const string DefaultGroupLinkTable = "DA_AcquisitionGroup_Config";

        private readonly IFileConfigRepository _repository;

        public FileConfigService(IFileConfigRepository repository)
        {
            _repository = repository;
        }

        #region Config 查询操作 (Read)

        /// <summary>
        /// 统一入口：根据 Options 路由到不同的查询逻辑
        /// </summary>
        public NancyModuleExtensions.PageResult<AcquisitionConfig> GetFileConfigsPaged(FileConfigQueryOptions options, int page, int limit)
        {
            options = options ?? new FileConfigQueryOptions();
            string tableName = string.IsNullOrEmpty(options.TableName) ? DefaultConfigTable : options.TableName;
            string dbName = string.IsNullOrEmpty(options.DatabaseName) ? DefaultDb : options.DatabaseName;

            // 调用刚刚在 Repository 写的元组返回方法
            var result = _repository.GetPageList(options, page, limit, tableName, dbName);

            return new NancyModuleExtensions.PageResult<AcquisitionConfig>
            {
                Total = result.Total,
                List = result.List
            };
        }

        /// <summary>
        /// 统一入口：根据 Options 路由到不同的查询逻辑
        /// </summary>
        public IEnumerable<AcquisitionConfig> GetFileConfigs(FileConfigQueryOptions options)
        {
            options = options ?? new FileConfigQueryOptions();

            // 1. 如果有 Task 过滤，需要穿透查询：Task -> Group -> Config
            if (options.HasTaskFilter)
            {
                return GetConfigsByTaskIds(options.TaskIds, options.TableName, options.DatabaseName);
            }

            // 2. 如果只有 Group 过滤：Group -> Config
            if (options.HasGroupFilter)
            {
                return GetConfigsByGroupIds(options.GroupIds, options.TableName, options.LinkTableName, options.DatabaseName);
            }

            // 3. 如果只有 Id 过滤：Config
            if (options.HasIdFilter)
            {
                return GetByIds(options.Ids, options.TableName, options.DatabaseName);
            }

            // 4. 基础查询：全表有效配置
            // 注意：这里也假设 options 里的 TableName 如果为空的话，底层 repository 有做处理，或者这里也可以赋默认值
            return _repository.GetList(
                string.IsNullOrEmpty(options.TableName) ? DefaultConfigTable : options.TableName,
                string.IsNullOrEmpty(options.DatabaseName) ? DefaultDb : options.DatabaseName
            );
        }

        /// <summary>
        /// 【核心】穿透查询：根据 Task ID 数组直接获取所有关联的 Configs
        /// </summary>
        public IEnumerable<AcquisitionConfig> GetConfigsByTaskIds(string[] taskIds, string configTableName = null, string databaseName = null)
        {
            if (taskIds == null || taskIds.Length == 0) return new List<AcquisitionConfig>();

            configTableName = string.IsNullOrEmpty(configTableName) ? DefaultConfigTable : configTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            return _repository.GetListByTaskIds(taskIds, configTableName, databaseName);
        }

        /// <summary>
        /// 组查询：合并了单组和多组逻辑
        /// </summary>
        public IEnumerable<AcquisitionConfig> GetConfigsByGroupIds(string[] groupIds, string configTableName = null, string configLinkTableName = null, string databaseName = null)
        {
            if (groupIds == null) return new List<AcquisitionConfig>();

            configTableName = string.IsNullOrEmpty(configTableName) ? DefaultConfigTable : configTableName;
            configLinkTableName = string.IsNullOrEmpty(configLinkTableName) ? DefaultGroupLinkTable : configLinkTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            return _repository.GetListByGroupIds(groupIds, configTableName, configLinkTableName, databaseName);
        }

        /// <summary>
        /// 根据 ID 数组获取配置
        /// </summary>
        public IEnumerable<AcquisitionConfig> GetByIds(string[] ids, string configTableName = null, string databaseName = null)
        {
            if (ids == null || ids.Length == 0) return new List<AcquisitionConfig>();

            configTableName = string.IsNullOrEmpty(configTableName) ? DefaultConfigTable : configTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            return _repository.GetListByIds(ids, configTableName, databaseName);
        }

        /// <summary>
        /// 根据ID列表查询配置状态
        /// </summary>
        public IEnumerable<AcquisitionConfigStatus> GetStatusByIds(string[] ids, string columnName = "EqName", string configTableName = null, string databaseName = null)
        {
            if (ids == null || ids.Length == 0) return new List<AcquisitionConfigStatus>();

            columnName = string.IsNullOrEmpty(columnName) ? "EqName" : columnName;
            configTableName = string.IsNullOrEmpty(configTableName) ? DefaultConfigTable : configTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            return _repository.GetStatusListByIds(ids, columnName, configTableName, databaseName);
        }

        #endregion

        #region Config 增删改操作 (CUD)

        /// <summary>
        /// 新增配置
        /// </summary>
        public int CreateConfig(AcquisitionConfig config, string configTableName = null, string databaseName = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            configTableName = string.IsNullOrEmpty(configTableName) ? DefaultConfigTable : configTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            return _repository.Insert(config, configTableName, databaseName);
        }

        /// <summary>
        /// 更新配置
        /// </summary>
        public bool UpdateConfig(AcquisitionConfig config, string configTableName = null, string databaseName = null)
        {
            if (config == null || config.Id <= 0) return false;

            configTableName = string.IsNullOrEmpty(configTableName) ? DefaultConfigTable : configTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            return _repository.Update(config, configTableName, databaseName);
        }

        /// <summary>
        /// 批量删除配置
        /// </summary>
        public bool DeleteConfigs(string[] ids, string configTableName = null, string databaseName = null)
        {
            if (ids == null || ids.Length == 0) return false;

            configTableName = string.IsNullOrEmpty(configTableName) ? DefaultConfigTable : configTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            return _repository.Delete(ids, configTableName, databaseName);
        }

        /// <summary>
        /// 批量设置状态
        /// </summary>
        public bool SetEnabledStatus(string[] ids, bool isEnabled, string configTableName = null, string databaseName = null)
        {
            if (ids == null || ids.Length == 0) return false;

            configTableName = string.IsNullOrEmpty(configTableName) ? DefaultConfigTable : configTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            return _repository.SetEnabled(ids, isEnabled, configTableName, databaseName);
        }

        #endregion
    }
}