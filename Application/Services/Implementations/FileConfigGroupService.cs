using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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

        public IEnumerable<AcquisitionGroup> GetList(string tableName = null, string dbName = null)
        {
            return _repository.GetList(tableName, dbName);
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
        public bool AddConfigsToGroup(int groupId, int[] configIds, string groupLinkTableName = null, string dbName = null)
        {

            // 【设计决策】使用 TransactionScopeAsyncFlowOption 以支持在 async 环境下传播事务上下文
            using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                try
                {
                    // 1. 删除该组现有的所有关联
                    _repository.RemoveAllConfigsFromGroup(groupId, groupLinkTableName);

                    // 2. 如果有新配置需要关联，执行批量写入
                    if (configIds != null && configIds.Length > 0)
                    {
                        DataTable dt = new DataTable();
                        dt.Columns.Add("GroupId", typeof(int));
                        dt.Columns.Add("ConfigId", typeof(int));
                        dt.Columns.Add("IsEnabled", typeof(bool));

                        foreach (var id in configIds)
                        {
                            dt.Rows.Add(groupId, id, true);
                        }

                        // 【异步调用】利用 BulkService 实现高性能写入
                        // 注意：由于 Service 接口通常非 async，这里使用 GetAwaiter().GetResult() 
                        // 确保同步阻塞等待完成，以符合当前接口定义。
                        _dataService.BulkInsertAsync(dt, groupLinkTableName).GetAwaiter().GetResult();
                    }

                    scope.Complete();
                    return true;
                }
                catch (Exception ex)
                {
                    //LogEntryBuilder.CreateError("FileConfigGroupService",
                    //    $"更新组 {groupId} 的配置关联失败: {ex.Message}").Write();
                    _ = ex.Message;
                    
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