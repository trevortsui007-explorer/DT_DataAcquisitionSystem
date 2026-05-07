using System.Collections.Generic;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    /// <summary>
    /// 数据采集任务仓库接口
    /// </summary>
    public interface IAcquisitionTaskRepository : IRepository
    {
        #region 基础查询 (Query)

        /// <summary>
        /// 获取所有任务列表
        /// </summary>
        IEnumerable<AcquisitionTask> GetList(
            string tableName = "DA_AcquisitionTask",
            string databaseName = "BaseDb");

        /// <summary>
        /// 获取所有任务列表，并附带任务关联的配置组信息
        /// </summary>
        IEnumerable<AcquisitionTaskDto> GetListWithGroups(
            string tableName = "DA_AcquisitionTask",
            string linkTable = "DA_AcquisitionTask_Group",
            string groupTable = "DA_AcquisitionGroup",
            string groupConfigLinkTable = "DA_AcquisitionGroup_Config",
            string databaseName = "BaseDb");

        /// <summary>
        /// 根据 ID 获取单个任务
        /// </summary>
        AcquisitionTask GetById(
            string id,
            string tableName = "DA_AcquisitionTask",
            string databaseName = "BaseDb");

        /// <summary>
        /// 根据 ID 获取单个任务，并附带任务关联的配置组信息
        /// </summary>
        AcquisitionTaskDto GetByIdWithGroups(
            string id,
            string tableName = "DA_AcquisitionTask",
            string linkTable = "DA_AcquisitionTask_Group",
            string groupTable = "DA_AcquisitionGroup",
            string groupConfigLinkTable = "DA_AcquisitionGroup_Config",
            string databaseName = "BaseDb");

        /// <summary>
        /// 根据 ID 集合获取任务列表
        /// </summary>
        IEnumerable<AcquisitionTask> GetListByIds(
            IEnumerable<string> ids,
            string tableName = "DA_AcquisitionTask",
            string databaseName = "BaseDb");

        /// <summary>
        /// 获取特定模式的任务 (例如只获取 TaskMode=1 的用于 Hangfire 初始化)
        /// </summary>
        IEnumerable<AcquisitionTask> GetListByMode(
            int taskMode,
            string tableName = "DA_AcquisitionTask",
            string databaseName = "BaseDb");

        #endregion

        #region 关联操作 (任务 - 配置组)
        /// 关联表: DA_AcquisitionTask_Group

        /// <summary>
        /// 将任务添加到指定配置组
        /// </summary>
        bool AddToGroups(
            int taskId,
            IEnumerable<int> groupIds,
            string linkTable = "DA_AcquisitionTask_Group",
            string databaseName = "BaseDb");

        /// <summary>
        /// 从指定配置组中移除任务
        /// </summary>
        bool RemoveFromGroups(
            int taskId,
            IEnumerable<int> groupIds,
            string linkTable = "DA_AcquisitionTask_Group",
            string databaseName = "BaseDb");

        /// <summary>
        /// 移除任务在所有组中的关联
        /// </summary>
        bool RemoveAllGroups(
            int taskId,
            string linkTable = "DA_AcquisitionTask_Group",
            string databaseName = "BaseDb");

        /// <summary>
        /// 获取任务所属的所有组 ID
        /// </summary>
        IEnumerable<int> GetGroupIdsByTaskId(
            int taskId,
            string linkTable = "DA_AcquisitionTask_Group",
            string databaseName = "BaseDb");

        #endregion
    }
}