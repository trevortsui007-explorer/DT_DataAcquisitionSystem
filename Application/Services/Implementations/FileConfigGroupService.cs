using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class FileConfigGroupService : IFileConfigGroupService
    {
        private readonly IFileConfigGroupRepository _repository;
        private readonly IDataService _dataService;

        public FileConfigGroupService(IFileConfigGroupRepository repository, IDataService dataService)
        {
            _repository = repository;
            _dataService = dataService;
        }

        #region 查询操作 (Read)

        public IEnumerable<AcquisitionGroupDto> GetList(string tableName = null, string linkTableName = null, string dbName = null)
        {
            return _repository.GetList(tableName, linkTableName, dbName);
        }

        public IEnumerable<AcquisitionGroup> GetByIds(string[] ids, string tableName = null, string dbName = null)
        {
            if (ids == null || ids.Length == 0) return Enumerable.Empty<AcquisitionGroup>();
            return _repository.GetListByIds(ids, tableName, dbName);
        }

        public IEnumerable<AcquisitionGroup> GetStatusByIds(string[] ids, string columnName, string tableName = null, string dbName = null)
        {
            if (ids == null || ids.Length == 0) return Enumerable.Empty<AcquisitionGroup>();

            // 注意：Repository 返回的是 AcquisitionConfigStatus，而接口定义返回 AcquisitionGroup
            // 这里为了适配接口定义，做一个简单的转换。建议长期来看，统一 DTO 或实体定义。
            var statusList = _repository.GetStatusListByIds(ids, columnName, tableName, dbName);

            return statusList.Select(s => new AcquisitionGroup
            {
                Id = s.Id,
                GroupName = s.Name,
                IsEnabled = s.IsEnabled
            });
        }

        #endregion

        #region 写入操作 (Create / Update / Delete)

        public int CreateConfigGroup(AcquisitionGroup group, string tableName = null, string dbName = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            return _repository.Insert(group, tableName, dbName);
        }

        public bool UpdateConfig(AcquisitionGroup group, string tableName = null, string dbName = null)
        {
            if (group == null || group.Id <= 0) return false;
            return _repository.Update(group, tableName, dbName);
        }

        public bool DeleteConfigs(string[] ids, string tableName = null, string dbName = null)
        {
            if (ids == null || ids.Length == 0) return false;
            return _repository.Delete(ids, tableName, dbName);
        }

        public bool SetEnabledStatus(string[] ids, bool isEnabled, string tableName = null, string dbName = null)
        {
            if (ids == null || ids.Length == 0) return false;
            return _repository.SetEnabled(ids, isEnabled, tableName, dbName);
        }

        #endregion

        #region 关联操作（配置 - 配置组）

        /// <summary>
        /// 批量建立关联关系 (使用 SqlBulkCopy 优化)
        /// </summary>
        public async Task<bool> AddConfigsToGroup(int groupId, int[] configIds, string tableName = null, string dbName = null)
        {
            // TransactionScope 必须开启 AsyncFlowOption 才能在 await 之后保持事务
            using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                try
                {
                    if (configIds != null && configIds.Length > 0)
                    {
                        DataTable dt = new DataTable();

                        dt.Columns.Add("GroupId", typeof(int));
                        dt.Columns.Add("ConfigId", typeof(int));
                        dt.Columns.Add("IsEnabled", typeof(bool));

                        if (configIds != null)
                        {
                            foreach (var id in configIds)
                            {
                                // 为每一个 ID 创建一行数据
                                dt.Rows.Add(groupId, id, true);
                            }
                        }

                        // 数据插入
                        await _dataService.BulkInsertAsync(dt, tableName);
                    }

                    scope.Complete();
                    return true;
                }
                catch (Exception ex)
                {
                    _ = ex.Message;
                    // 记录日志
                    return false;
                }
            }
        }

        /// <summary>
        /// 批量解除关联关系
        /// </summary>
        public bool RemoveConfigsFromGroup(int groupId, int[] configIds, string tableName = null, string dbName = null)
        {
            if (configIds == null || configIds.Length == 0) return true;
            return _repository.RemoveConfigsFromGroup(groupId, configIds, tableName, dbName);
        }

        #endregion
    }
}