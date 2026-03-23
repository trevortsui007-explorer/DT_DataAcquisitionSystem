using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using Microsoft.Practices.Unity;

namespace DT_DataAcquisitionSystem.Infrastructure.Persistence
{
    /// <summary>
    /// 提供高性能的数据库批量写入服务。
    /// 采用 SqlBulkCopy 技术，旨在解决大规模数据导入时的性能瓶颈。
    /// </summary>
    public class SqlDataService : IDataService
    {
        private readonly string _connectionString;

        [InjectionConstructor]
        public SqlDataService()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["BaseDb"].ConnectionString;
        }

        public SqlDataService(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// 获取目标表的元数据结构。
        /// </summary>
        /// <param name="tableName">目标表名称</param>
        /// <returns>返回包含表结构的空 DataTable</returns>
        public async Task<DataTable> GetTableSchemaAsync(string tableName)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);

                // 仅获取 Schema 而不读取数据，避免全表扫描
                string sql = $"SELECT TOP 0 * FROM [{tableName}]";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    // 【优化】添加 CommandBehavior.KeyInfo，确保带回 AutoIncrement、AllowDBNull 等完整元数据
                    using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo))
                    {
                        var dt = new DataTable();
                        dt.Load(reader);
                        return dt;
                    }
                }
            }
        }

        /// <summary>
        /// 将字典列表转换为强类型 DataTable。
        /// 解决从弱类型数据源 (CSV/JSON) 到数据库强类型匹配的逻辑转换。
        /// </summary>
        public DataTable PopulateDataTable(IEnumerable<IDictionary<string, object>> data, DataTable schema)
        {
            DataTable dt = schema.Clone();
            foreach (var item in data)
            {
                DataRow dr = dt.NewRow();
                foreach (DataColumn column in dt.Columns)
                {
                    string colName = column.ColumnName;
                    Type targetType = Nullable.GetUnderlyingType(column.DataType) ?? column.DataType;

                    // 1. 获取原始值
                    item.TryGetValue(colName, out var val);

                    // 2. 检查值是否有效 (处理 null, DBNull, 空字符串)
                    bool isValueInvalid = val == null || val == DBNull.Value || string.IsNullOrWhiteSpace(val.ToString());

                    if (isValueInvalid)
                    {
                        // 处理空值：如果是自增列则跳过，否则尝试给非空列赋默认值
                        if (!column.AutoIncrement)
                        {
                            dr[colName] = column.AllowDBNull ? DBNull.Value : GetDefaultValue(targetType);
                        }
                        continue;
                    }

                    // 3. 核心转换逻辑
                    try
                    {
                        Type sourceType = val.GetType();

                        if (targetType == sourceType)
                        {
                            dr[colName] = val;
                        }
                        else if (targetType == typeof(Guid))
                        {
                            // 目标是 Guid，源可能是 string
                            dr[colName] = val is string s ? Guid.Parse(s) : Convert.ChangeType(val, typeof(Guid));
                        }
                        else if (targetType == typeof(string))
                        {
                            // 目标是 string (varchar)，源可能是 Guid 或其他
                            dr[colName] = val.ToString();
                        }
                        else
                        {
                            // 其他通用转换
                            dr[colName] = Convert.ChangeType(val, targetType);
                        }
                    }
                    catch
                    {
                        if (column.AllowDBNull) dr[colName] = DBNull.Value;
                        else throw new InvalidOperationException($"列 {colName} 无法将 {val.GetType()} 转换为 {targetType}");
                    }
                }
                dt.Rows.Add(dr);
            }
            return dt;
        }

        /// <summary>
        /// 执行批量插入。
        /// </summary>
        /// <param name="dataTable">待插入数据源</param>
        /// <param name="destinationTableName">目标表</param>
        /// <param name="ct">取消令牌</param>
        public async Task BulkInsertAsync(DataTable dataTable, string destinationTableName, CancellationToken ct = default)
        {
            if (dataTable == null || dataTable.Rows.Count == 0) return;

            // TableLock 提升写入性能，如遇高并发死锁可按需移除
            using (var bulkCopy = new SqlBulkCopy(_connectionString, SqlBulkCopyOptions.Default | SqlBulkCopyOptions.TableLock))
            {
                bulkCopy.DestinationTableName = $"[{destinationTableName}]";
                bulkCopy.BatchSize = 50000;
                bulkCopy.BulkCopyTimeout = 600;

                foreach (DataColumn column in dataTable.Columns)
                {
                    // 【优化】智能过滤自增列，交由数据库底层自动递增
                    if (column.AutoIncrement)
                    {
                        continue;
                    }

                    // 检查这批数据中，该列是否全部都是空值(DBNull)
                    bool isAllNull = true;
                    foreach (DataRow row in dataTable.Rows)
                    {
                        if (!row.IsNull(column)) // 只要有一行有真实数据，就不能跳过映射
                        {
                            isAllNull = false;
                            break;
                        }
                    }

                    // 如果这一批数据里这一列全都是空，且数据库表结构里这一列不允许为空（NOT NULL）
                    // 说明数据库大概率有 DEFAULT 约束（如 NEWID() 或 GETDATE()），此时跳过映射，交由底层触发默认值
                    if (isAllNull && !column.AllowDBNull)
                    {
                        continue;
                    }

                    bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                }

                await bulkCopy.WriteToServerAsync(dataTable, ct);
            }
        }

        /// <summary>
        /// 执行存储过程触发后处理逻辑。
        /// </summary>
        public async Task ExecuteStoredProcedureAsync(string flag, string sprocName, CancellationToken ct = default)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                using (var cmd = new SqlCommand(sprocName, conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandTimeout = 600;

                    if (!string.IsNullOrEmpty(flag))
                    {
                        cmd.Parameters.AddWithValue("@Flag", flag);
                    }
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        #region 辅助方法

        // 默认值生成逻辑
        private object GetDefaultValue(Type t)
        {
            if (t == typeof(string)) return string.Empty;
            if (t == typeof(Guid)) return Guid.NewGuid();
            if (t == typeof(DateTime)) return DateTime.UtcNow;
            if (t == typeof(bool)) return false;
            if (t.IsValueType) return Activator.CreateInstance(t); // 处理所有数值类型
            return DBNull.Value;
        }

        #endregion
    }
}