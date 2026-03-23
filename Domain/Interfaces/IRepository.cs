using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    /// <summary>
    /// 基础仓库接口（同步版）
    /// </summary>
    public interface IRepository
    {
        // --- 增 ---
        int Insert<T>(T entity, string tableName, string databaseName) where T : class;

        // --- 改 ---
        bool Update<T>(T entity, string tableName, string databaseName) where T : class;

        // --- 硬删 ---
        bool Delete<T>(IEnumerable<T> keyValue, string tableName, string databaseName) where T : class;
        
        // --- 软删 ---
        bool SetEnabled<T>(IEnumerable<T> keyValue, bool isEnabled, string tableName, string databaseName) where T : class;
    }
}