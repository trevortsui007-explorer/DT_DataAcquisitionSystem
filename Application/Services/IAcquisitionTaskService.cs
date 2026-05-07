using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    /// <summary>
    /// 数据采集任务服务接口
    /// </summary>
    public interface IAcquisitionTaskService
    {
        #region 查询操作 (Read)

        /// <summary>
        /// 获取所有任务列表，包含关联配置组信息
        /// </summary>
        IEnumerable<AcquisitionTaskDto> GetList(
            string tableName = null,
            string dbName = null);

        /// <summary>
        /// 根据 ID 获取单个任务详情，包含关联配置组信息
        /// </summary>
        AcquisitionTaskDto GetById(
            string id,
            string tableName = null,
            string dbName = null);

        /// <summary>
        /// 根据 ID 集合获取任务列表
        /// </summary>
        IEnumerable<AcquisitionTask> GetByIds(
            string[] ids,
            string tableName = null,
            string dbName = null);

        /// <summary>
        /// 根据任务模式获取任务列表 (如：定时模式、循环模式)
        /// </summary>
        IEnumerable<AcquisitionTask> GetByMode(
            int taskMode,
            string tableName = null,
            string dbName = null);

        /// <summary>
        /// 获取任务已关联的配置组 ID 集合
        /// </summary>
        IEnumerable<int> GetAssociatedGroupIds(
            int taskId,
            string linkTable = null,
            string dbName = null);

        #endregion

        #region 写入操作 (Create / Update / Delete)

        /// <summary>
        /// 创建新任务
        /// </summary>
        int CreateTask(
            AcquisitionTask entity,
            string tableName = null,
            string dbName = null);

        /// <summary>
        /// 更新任务基本信息
        /// </summary>
        bool UpdateTask(
            AcquisitionTask entity,
            string tableName = null,
            string dbName = null);

        /// <summary>
        /// 批量删除任务
        /// </summary>
        bool DeleteTasks(
            string[] ids,
            string tableName = null,
            string dbName = null);

        /// <summary>
        /// 批量启用/禁用任务
        /// </summary>
        bool SetEnabledStatus(
            string[] ids,
            bool isEnabled,
            string tableName = null,
            string dbName = null);

        #endregion

        #region 关联操作 (Task - Group Relation)

        /// <summary>
        /// 为任务重新分配配置组 (全量更新：先删后插)
        /// </summary>
        bool AssignGroupsToTask(
            int taskId,
            int[] groupIds,
            string linkTableName = null,
            string dbName = null);

        #endregion
    }
}