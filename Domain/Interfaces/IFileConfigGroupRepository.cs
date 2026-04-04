using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Application.DTOs;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface IFileConfigGroupRepository : IRepository
    {
        // --- 组查询 ---
        /// <summary>
        /// 获取全部配置
        /// </summary>
        /// <returns></returns>
        IEnumerable<AcquisitionGroupDto> GetList(string tableName = null, string linkTableName = null, string databaseName = null);

        /// <summary>
        /// 根据Ids获取配置
        /// </summary>
        /// <param name="ids">配置ID集合</param>
        /// <returns></returns>
        IEnumerable<AcquisitionGroup> GetListByIds(IEnumerable<string> ids, string tableName, string databaseName);

        /// <summary>
        /// 根据Ids获取配置状态
        /// </summary>
        /// <param name="ids">配置ID集合</param>
        /// <returns></returns>
        IEnumerable<AcquisitionConfigStatus> GetStatusListByIds(IEnumerable<string> ids, string columnName, string tableName, string databaseName);

        // --- 关联操作（配置 - 配置组）---

        bool AddConfigsToGroup(int groupId, int[] configIds, string tableName = null, string groupLinkTableName = null, string dbName = null);

        bool RemoveConfigsFromGroup(int groupId, int[] configIds, string groupLinkTableName = null, string dbName = null);
        bool RemoveAllConfigsFromGroup(int groupId, string groupLinkTableName = null, string dbName = null);
    }
}