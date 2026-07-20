using System;
using System.Collections.Generic;
using System.Linq;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Common.Extensions;
using DT_DataAcquisitionSystem.Common.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            config.ParserType = string.IsNullOrWhiteSpace(config.ParserType) ? "standard-table" : config.ParserType;
            config.ParserOptions = ProtectFileAccessOptions(config.ParserOptions, null);

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
            config.ParserType = string.IsNullOrWhiteSpace(config.ParserType) ? "standard-table" : config.ParserType;
            var existing = GetByIds(new[] { config.Id.ToString() }, configTableName, databaseName).FirstOrDefault();
            config.ParserOptions = ProtectFileAccessOptions(config.ParserOptions, existing?.ParserOptions);

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

        private static string ProtectFileAccessOptions(string parserOptions, string existingParserOptions)
        {
            JObject root = ParseOptions(parserOptions);
            if (root == null) return parserOptions;

            JObject existingRoot = ParseOptions(existingParserOptions);
            JObject fileAccess = GetObjectIgnoreCase(root, "fileAccess");
            JObject existingFileAccess = GetObjectIgnoreCase(existingRoot, "fileAccess");

            if (fileAccess == null)
            {
                if (existingFileAccess != null)
                {
                    root["fileAccess"] = existingFileAccess.DeepClone();
                }

                return root.ToString(Formatting.None);
            }

            bool useCurrentWindowsIdentity = GetBoolIgnoreCase(fileAccess, "useCurrentWindowsIdentity");
            fileAccess["useCurrentWindowsIdentity"] = useCurrentWindowsIdentity;
            if (useCurrentWindowsIdentity)
            {
                RemovePropertyIgnoreCase(fileAccess, "domain");
                RemovePropertyIgnoreCase(fileAccess, "userName");
                RemovePropertyIgnoreCase(fileAccess, "passwordPlain");
                RemovePropertyIgnoreCase(fileAccess, "passwordProtected");
                RemovePropertyIgnoreCase(fileAccess, "passwordSet");
                RemovePropertyIgnoreCase(fileAccess, "clearPassword");
                return root.ToString(Formatting.None);
            }

            bool clearPassword = GetBoolIgnoreCase(fileAccess, "clearPassword");
            string plainPassword = GetStringIgnoreCase(fileAccess, "passwordPlain");
            string existingProtected = GetStringIgnoreCase(existingFileAccess, "passwordProtected");

            RemovePropertyIgnoreCase(fileAccess, "passwordPlain");
            RemovePropertyIgnoreCase(fileAccess, "clearPassword");

            if (clearPassword)
            {
                RemovePropertyIgnoreCase(fileAccess, "passwordProtected");
                RemovePropertyIgnoreCase(fileAccess, "passwordSet");
            }
            else if (!string.IsNullOrEmpty(plainPassword))
            {
                fileAccess["passwordProtected"] = CredentialProtector.Protect(plainPassword);
                fileAccess["passwordSet"] = true;
            }
            else if (string.IsNullOrWhiteSpace(GetStringIgnoreCase(fileAccess, "passwordProtected")) &&
                     !string.IsNullOrWhiteSpace(existingProtected))
            {
                fileAccess["passwordProtected"] = existingProtected;
                fileAccess["passwordSet"] = true;
            }

            return root.ToString(Formatting.None);
        }

        private static JObject ParseOptions(string parserOptions)
        {
            if (string.IsNullOrWhiteSpace(parserOptions)) return null;

            try
            {
                return JObject.Parse(parserOptions);
            }
            catch
            {
                return null;
            }
        }

        private static JObject GetObjectIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName)) return null;

            foreach (var property in source.Properties())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value as JObject;
                }
            }

            return null;
        }

        private static string GetStringIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName)) return null;

            foreach (var property in source.Properties())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value?.ToString();
                }
            }

            return null;
        }

        private static bool GetBoolIgnoreCase(JObject source, string propertyName)
        {
            string value = GetStringIgnoreCase(source, propertyName);
            return bool.TryParse(value, out bool result) && result;
        }

        private static void RemovePropertyIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName)) return;

            var property = source.Properties()
                .FirstOrDefault(x => string.Equals(x.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            property?.Remove();
        }
    }
}
