using DT_DataAcquisitionSystem.Domain.Entities;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IFileConfigGroupService
    {
        #region 查询操作
        /// <summary>
        /// 根据 ID 数组获取组列表
        /// </summary>
        IEnumerable<AcquisitionGroup> GetByIds(string[] ids, string tableName = null, string dbName = null);

        /// <summary>
        /// 获取所有组（通常用于下拉框或列表展示）
        /// </summary>
        IEnumerable<AcquisitionGroup> GetList(string tableName = null, string dbName = null);

        /// <summary>
        /// 获取状态信息
        /// </summary>
        IEnumerable<AcquisitionGroup> GetStatusByIds(string[] ids, string columnName, string tableName = null, string dbName = null);
        #endregion

        #region 写入操作
        int CreateConfigGroup(AcquisitionGroup configGroup, string tableName = null, string dbName = null);

        bool UpdateConfig(AcquisitionGroup configGroup, string tableName = null, string dbName = null);

        bool DeleteConfigs(string[] ids, string tableName = null, string dbName = null);

        bool SetEnabledStatus(string[] ids, bool isEnabled, string tableName = null, string dbName = null);
        #endregion

        #region 关联操作（配置 - 配置组）

        bool AddConfigsToGroup(int groupId, int[] configIds, string tableName = null, string dbName = null);
        bool RemoveConfigsFromGroup(int groupId, int[] configIds, string tableName = null, string dbName = null);

        #endregion
    }
}