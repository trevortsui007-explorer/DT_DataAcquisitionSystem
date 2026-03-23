using System.Collections.Generic;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    /// <summary>
    /// 文件配置仓库接口
    /// </summary>
    public interface IFileConfigRepository : IRepository
    {
        /// <summary>
        /// 获取全部配置
        /// </summary>
        /// <returns></returns>
        IEnumerable<AcquisitionConfig> GetList(string tableName = null, string databaseName = null);

        /// <summary>
        /// 根据Ids获取配置
        /// </summary>
        /// <param name="ids">配置ID集合</param>
        /// <returns></returns>
        IEnumerable<AcquisitionConfig> GetListByIds(IEnumerable<string> ids, string tableName = null, string databaseName = null);

        /// <summary>
        /// 根据GroupIds获取配置
        /// </summary>
        /// <param name="groupIds">分组ID集合</param>
        /// <returns></returns>
        IEnumerable<AcquisitionConfig> GetListByGroupIds(IEnumerable<string> groupIds, string tableName = null,string linkTableName = null, string databaseName = null);

        /// <summary>
        /// 根据TaskIds获取配置
        /// </summary>
        /// <param name="taskIds">任务ID集合</param>
        /// <returns></returns>
        IEnumerable<AcquisitionConfig> GetListByTaskIds(IEnumerable<string> taskIds, string tableName = null, string databaseName = null);

        /// <summary>
        /// 根据Ids获取配置状态
        /// </summary>
        /// <param name="ids">配置ID集合</param>
        /// <returns></returns>
        IEnumerable<AcquisitionConfigStatus> GetStatusListByIds(IEnumerable<string> ids, string columnName = null, string tableName = null, string databaseName = null);
    }
}