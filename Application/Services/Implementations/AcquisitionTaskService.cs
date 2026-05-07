using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Transactions;

namespace DT_DataAcquisitionSystem.Application.Services
{
    /// <summary>
    /// 采集任务应用服务：负责业务流程编排与事务控制。
    /// </summary>
    public class AcquisitionTaskService : IAcquisitionTaskService
    {
        private readonly IAcquisitionTaskRepository _repository;
        private readonly IDataService _dataService;

        /// <summary>
        /// 构造函数：注入仓储与数据服务。
        /// </summary>
        public AcquisitionTaskService(IAcquisitionTaskRepository repository, IDataService dataService)
        {
            _repository = repository;
            _dataService = dataService;
        }

        #region 查询操作 (Read)

        /// <summary>
        /// 获取所有采集任务列表，包含关联配置组信息。
        /// </summary>
        public IEnumerable<AcquisitionTaskDto> GetList(string tableName = null, string dbName = null)
        {
            tableName = string.IsNullOrEmpty(tableName) ? "DA_AcquisitionTask" : tableName;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            return _repository.GetListWithGroups(
                tableName: tableName,
                linkTable: "DA_AcquisitionTask_Group",
                groupTable: "DA_AcquisitionGroup",
                groupConfigLinkTable: "DA_AcquisitionGroup_Config",
                databaseName: dbName
            );
        }

        /// <summary>
        /// 根据主键 ID 获取单个采集任务，包含关联配置组信息。
        /// </summary>
        public AcquisitionTaskDto GetById(string id, string tableName = null, string dbName = null)
        {
            if (string.IsNullOrEmpty(id)) return null;

            tableName = string.IsNullOrEmpty(tableName) ? "DA_AcquisitionTask" : tableName;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            return _repository.GetByIdWithGroups(
                id: id,
                tableName: tableName,
                linkTable: "DA_AcquisitionTask_Group",
                groupTable: "DA_AcquisitionGroup",
                groupConfigLinkTable: "DA_AcquisitionGroup_Config",
                databaseName: dbName
            );
        }

        /// <summary>
        /// 根据 ID 数组批量获取采集任务。
        /// 这里仍然返回实体，主要用于内部调度或兼容旧逻辑。
        /// </summary>
        public IEnumerable<AcquisitionTask> GetByIds(string[] ids, string tableName = null, string dbName = null)
        {
            if (ids == null || ids.Length == 0) return Enumerable.Empty<AcquisitionTask>();

            tableName = string.IsNullOrEmpty(tableName) ? "DA_AcquisitionTask" : tableName;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            return _repository.GetListByIds(ids, tableName, dbName);
        }

        /// <summary>
        /// 根据任务模式筛选采集任务。
        /// 这里仍然返回实体，避免影响 Hangfire 初始化逻辑。
        /// </summary>
        public IEnumerable<AcquisitionTask> GetByMode(int taskMode, string tableName = null, string dbName = null)
        {
            tableName = string.IsNullOrEmpty(tableName) ? "DA_AcquisitionTask" : tableName;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            return _repository.GetListByMode(taskMode, tableName, dbName);
        }

        /// <summary>
        /// 获取任务关联的配置组 ID 集合。
        /// </summary>
        public IEnumerable<int> GetAssociatedGroupIds(int taskId, string linkTable = null, string dbName = null)
        {
            linkTable = string.IsNullOrEmpty(linkTable) ? "DA_AcquisitionTask_Group" : linkTable;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            return _repository.GetGroupIdsByTaskId(taskId, linkTable, dbName);
        }

        #endregion

        #region 写入操作 (Create / Update / Delete)

        /// <summary>
        /// 创建新任务并自动维护审计时间。
        /// </summary>
        public int CreateTask(AcquisitionTask entity, string tableName = null, string dbName = null)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            tableName = string.IsNullOrEmpty(tableName) ? "DA_AcquisitionTask" : tableName;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            entity.CreateTime = DateTime.Now;
            entity.UpdateTime = DateTime.Now;

            return _repository.Insert(entity, tableName, dbName);
        }

        /// <summary>
        /// 更新现有任务信息并同步更新时间。
        /// </summary>
        public bool UpdateTask(AcquisitionTask entity, string tableName = null, string dbName = null)
        {
            if (entity == null || entity.Id <= 0) return false;

            tableName = string.IsNullOrEmpty(tableName) ? "DA_AcquisitionTask" : tableName;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            entity.UpdateTime = DateTime.Now;

            return _repository.Update(entity, tableName, dbName);
        }

        /// <summary>
        /// 批量删除指定的采集任务。
        /// Repository 内部会先删除 DA_AcquisitionTask_Group 关联关系。
        /// </summary>
        public bool DeleteTasks(string[] ids, string tableName = null, string dbName = null)
        {
            if (ids == null || ids.Length == 0) return false;

            tableName = string.IsNullOrEmpty(tableName) ? "DA_AcquisitionTask" : tableName;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            return _repository.Delete(ids, tableName, dbName);
        }

        /// <summary>
        /// 批量更新任务的启用/禁用状态。
        /// </summary>
        public bool SetEnabledStatus(string[] ids, bool isEnabled, string tableName = null, string dbName = null)
        {
            if (ids == null || ids.Length == 0) return false;

            tableName = string.IsNullOrEmpty(tableName) ? "DA_AcquisitionTask" : tableName;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            return _repository.SetEnabled(ids, isEnabled, tableName, dbName);
        }

        #endregion

        #region 关联操作 (任务 - 配置组)

        /// <summary>
        /// 重新分配任务与配置组的关系（先删后增，含事务与性能优化）。
        /// </summary>
        public bool AssignGroupsToTask(int taskId, int[] groupIds, string linkTableName = null, string dbName = null)
        {
            linkTableName = string.IsNullOrEmpty(linkTableName) ? "DA_AcquisitionTask_Group" : linkTableName;
            dbName = string.IsNullOrEmpty(dbName) ? "BaseDb" : dbName;

            using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                try
                {
                    _repository.RemoveAllGroups(taskId, linkTableName, dbName);

                    if (groupIds != null && groupIds.Length > 0)
                    {
                        DataTable dt = new DataTable();
                        dt.Columns.Add("TaskId", typeof(int));
                        dt.Columns.Add("GroupId", typeof(int));

                        foreach (var gid in groupIds)
                        {
                            dt.Rows.Add(taskId, gid);
                        }

                        _dataService.BulkInsertAsync(dt, linkTableName).GetAwaiter().GetResult();
                    }

                    scope.Complete();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        #endregion
    }
}