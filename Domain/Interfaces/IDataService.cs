using DT_DataAcquisitionSystem.Domain.Entities;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface IDataService
    {
        /// <summary>
        /// 获取目标表的架构
        /// </summary>
        Task<DataTable> GetTableSchemaAsync(string tableName);

        /// <summary>
        /// 将解析后的原始数据填入具有表架构的 DataTable 中（核心：类型转换）
        /// </summary>
        DataTable PopulateDataTable(IEnumerable<IDictionary<string, object>> data, DataTable schema);

        /// <summary>
        /// 高性能批量入库
        /// </summary>
        Task BulkInsertAsync(DataTable dataTable, string destinationTableName, CancellationToken ct = default);

        /// <summary>
        /// 执行存储过程
        /// </summary>
        Task ExecuteStoredProcedureAsync(string flag, string sprocName, CancellationToken ct = default);

        /// <summary>
        /// 检查表是否存在，如果不存在则根据指定的列定义自动创建表。
        /// </summary>
        Task CreateTableIfNotExistsAsync(string tableName, IEnumerable<ColumnDefinition> columns, CancellationToken ct = default);
    }
}