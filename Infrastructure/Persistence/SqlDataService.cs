using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
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
                    TryGetValueIgnoreCase(item, colName, out var val);

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
                        else if (targetType == typeof(DateTime))
                        {
                            dr[colName] = ConvertToDateTime(val);
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

        private static bool TryGetValueIgnoreCase(IDictionary<string, object> item, string key, out object value)
        {
            if (item.TryGetValue(key, out value)) return true;

            foreach (var kvp in item)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static DateTime ConvertToDateTime(object value)
        {
            if (value is DateTime dateTime) return dateTime;

            string text = Convert.ToString(value)?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return Convert.ToDateTime(value);

            string[] formats =
            {
                "yyyy/MM/dd(HH:mm:ss)",
                "yyyy/M/d(H:m:s)",
                "yyyy/MM/dd HH:mm:ss",
                "yyyy/M/d H:m:s",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-M-d H:m:s",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-M-dTH:m:s"
            };

            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                return parsed;
            }

            return Convert.ToDateTime(value);
        }

        /// <summary>
        /// 执行批量插入。
        /// </summary>
        /// <param name="dataTable">待插入数据源</param>
        /// <param name="destinationTableName">目标表</param>
        /// <param name="ct">取消令牌</param>
        public async Task BulkInsertAsync(DataTable dataTable, string destinationTableName, CancellationToken ct = default)
        {
            if (dataTable == null) return;

            // TableLock 提升写入性能，如遇高并发死锁可按需移除
            using (var bulkCopy = new SqlBulkCopy(_connectionString, SqlBulkCopyOptions.Default))
            {
                bulkCopy.DestinationTableName = $"[{destinationTableName}]";
                bulkCopy.BatchSize = 50000;
                bulkCopy.BulkCopyTimeout = 600;

                AddColumnMappings(bulkCopy, dataTable);

                await bulkCopy.WriteToServerAsync(dataTable, ct);
            }
        }

        public async Task ReplaceFileDataAsync(DataTable dataTable, string destinationTableName, string fullPath, string fileName, DateTime businessDate, CancellationToken ct = default)
        {
            if (dataTable == null || dataTable.Rows.Count == 0) return;

            DataColumn fullPathColumn = FindColumn(dataTable, "FullFilePath");
            DataColumn fileNameColumn = FindColumn(dataTable, "FileName");
            DataColumn businessDateColumn = FindColumn(dataTable, "BusinessDate");

            bool useFullPath = fullPathColumn != null && !string.IsNullOrWhiteSpace(fullPath);
            bool useFileNameAndDate = fileNameColumn != null && businessDateColumn != null && !string.IsNullOrWhiteSpace(fileName);

            if (!useFullPath && !useFileNameAndDate)
            {
                throw new InvalidOperationException($"全量覆盖需要目标表 {destinationTableName} 包含 FullFilePath，或同时包含 FileName 与 BusinessDate。");
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        string deleteSql;
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandTimeout = 600;

                            if (useFullPath)
                            {
                                deleteSql = $"DELETE FROM [{destinationTableName}] WHERE [{fullPathColumn.ColumnName}] = @FullPath";
                                cmd.Parameters.Add("@FullPath", SqlDbType.NVarChar, 2000).Value = fullPath;
                            }
                            else
                            {
                                deleteSql = $"DELETE FROM [{destinationTableName}] WHERE [{fileNameColumn.ColumnName}] = @FileName AND [{businessDateColumn.ColumnName}] = @BusinessDate";
                                cmd.Parameters.Add("@FileName", SqlDbType.NVarChar, 500).Value = fileName;
                                cmd.Parameters.Add("@BusinessDate", SqlDbType.Date).Value = businessDate.Date;
                            }

                            cmd.CommandText = deleteSql;
                            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        if (dataTable.Rows.Count > 0)
                        {
                            using (var bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx))
                            {
                                bulkCopy.DestinationTableName = $"[{destinationTableName}]";
                                bulkCopy.BatchSize = 50000;
                                bulkCopy.BulkCopyTimeout = 600;
                                AddColumnMappings(bulkCopy, dataTable);
                                await bulkCopy.WriteToServerAsync(dataTable, ct).ConfigureAwait(false);
                            }
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        public async Task<int> DeleteInsertedRowsAsync(DataTable dataTable, string destinationTableName, string fullPath, string fileName, DateTime businessDate, CancellationToken ct = default)
        {
            if (dataTable == null || dataTable.Rows.Count == 0) return 0;

            DataColumn idColumn = FindColumn(dataTable, "Id");
            var ids = new List<string>();
            if (idColumn != null)
            {
                foreach (DataRow row in dataTable.Rows)
                {
                    if (row.IsNull(idColumn)) continue;
                    string id = Convert.ToString(row[idColumn])?.Trim();
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                }
            }

            if (ids.Count > 0)
            {
                return await DeleteRowsByIdsAsync(destinationTableName, ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ct)
                    .ConfigureAwait(false);
            }

            DataColumn fullPathColumn = FindColumn(dataTable, "FullFilePath");
            DataColumn sourceRowColumn = FindColumn(dataTable, "SourceRow");
            if (sourceRowColumn == null)
            {
                sourceRowColumn = FindColumn(dataTable, "row");
            }

            if (fullPathColumn == null || sourceRowColumn == null)
            {
                throw new InvalidOperationException($"后处理失败回滚需要目标表 {destinationTableName} 包含 Id，或同时包含 FullFilePath 与 SourceRow。");
            }

            var sourceRows = new HashSet<int>();
            foreach (DataRow row in dataTable.Rows)
            {
                if (row.IsNull(sourceRowColumn)) continue;
                int sourceRow;
                if (int.TryParse(Convert.ToString(row[sourceRowColumn]), out sourceRow) && sourceRow > 0)
                {
                    sourceRows.Add(sourceRow);
                }
            }

            if (sourceRows.Count == 0)
            {
                return 0;
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandTimeout = 600;
                    cmd.CommandText = BuildDeleteRowsByPathAndSourceRowsSql(destinationTableName, fullPathColumn.ColumnName, sourceRowColumn.ColumnName, sourceRows.Count);
                    cmd.Parameters.Add("@FullFilePath", SqlDbType.NVarChar, 2000).Value = string.IsNullOrWhiteSpace(fullPath) ? DBNull.Value : (object)fullPath;

                    int index = 0;
                    foreach (int sourceRow in sourceRows)
                    {
                        cmd.Parameters.Add("@Row" + index, SqlDbType.Int).Value = sourceRow;
                        index++;
                    }

                    return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
        }

        private async Task<int> DeleteRowsByIdsAsync(string destinationTableName, List<string> ids, CancellationToken ct)
        {
            int deleted = 0;
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                for (int offset = 0; offset < ids.Count; offset += 500)
                {
                    List<string> batch = ids.Skip(offset).Take(500).ToList();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandTimeout = 600;
                        var parameterNames = new List<string>();
                        for (int i = 0; i < batch.Count; i++)
                        {
                            string parameterName = "@Id" + i;
                            parameterNames.Add(parameterName);
                            cmd.Parameters.Add(parameterName, SqlDbType.NVarChar, 100).Value = batch[i];
                        }

                        cmd.CommandText = $"DELETE FROM [{destinationTableName}] WHERE CONVERT(NVARCHAR(100), [Id]) IN ({string.Join(",", parameterNames)})";
                        deleted += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                }
            }

            return deleted;
        }

        private static string BuildDeleteRowsByPathAndSourceRowsSql(string destinationTableName, string fullPathColumnName, string sourceRowColumnName, int sourceRowCount)
        {
            var parameterNames = Enumerable.Range(0, sourceRowCount).Select(i => "@Row" + i);
            return $"DELETE FROM [{destinationTableName}] WHERE [{fullPathColumnName}] = @FullFilePath AND [{sourceRowColumnName}] IN ({string.Join(",", parameterNames)})";
        }

        private static void AddColumnMappings(SqlBulkCopy bulkCopy, DataTable dataTable)
        {
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
                    if (!row.IsNull(column))
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
        }

        private static DataColumn FindColumn(DataTable dataTable, string columnName)
        {
            if (dataTable == null || string.IsNullOrWhiteSpace(columnName)) return null;

            foreach (DataColumn column in dataTable.Columns)
            {
                if (string.Equals(column.ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return column;
                }
            }

            return null;
        }
        /// <summary>
        /// 执行存储过程触发后处理逻辑。
        /// </summary>
        public async Task ExecuteStoredProcedureAsync(string flag, string sprocName, CancellationToken ct = default)
        {
            const int maxDeadlockRetries = 3;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await ExecuteStoredProcedureOnceAsync(flag, sprocName, ct).ConfigureAwait(false);
                    return;
                }
                catch (SqlException ex) when (IsDeadlock(ex) && attempt < maxDeadlockRetries)
                {
                    int delayMs = 500 * (attempt + 1) * (attempt + 1);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                catch (SqlException ex) when (IsDeadlock(ex))
                {
                    throw new Exception($"存储过程后处理发生死锁，已重试 {maxDeadlockRetries} 次仍失败: {ex.Message}", ex);
                }
            }
        }

        private async Task ExecuteStoredProcedureOnceAsync(string flag, string sprocName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sprocName)) throw new ArgumentNullException(nameof(sprocName));

            string lockResource = BuildPostProcessingLockResource(flag, sprocName);
            bool lockAcquired = false;

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                try
                {
                    await AcquirePostProcessingLockAsync(conn, lockResource, ct).ConfigureAwait(false);
                    lockAcquired = true;

                    using (var cmd = new SqlCommand(sprocName, conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.CommandTimeout = 600;

                        if (!string.IsNullOrEmpty(flag))
                        {
                            cmd.Parameters.AddWithValue("@Flag", flag);
                        }

                        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (lockAcquired)
                    {
                        await ReleasePostProcessingLockAsync(conn, lockResource).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task AcquirePostProcessingLockAsync(SqlConnection conn, string lockResource, CancellationToken ct)
        {
            using (var cmd = new SqlCommand("sp_getapplock", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 600;
                cmd.Parameters.AddWithValue("@Resource", lockResource);
                cmd.Parameters.AddWithValue("@LockMode", "Exclusive");
                cmd.Parameters.AddWithValue("@LockOwner", "Session");
                cmd.Parameters.AddWithValue("@LockTimeout", 600000);

                var returnValue = cmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
                returnValue.Direction = ParameterDirection.ReturnValue;

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                int lockResult = returnValue.Value == null ? -999 : Convert.ToInt32(returnValue.Value);
                if (lockResult < 0)
                {
                    throw new TimeoutException($"获取后处理执行锁失败，LockResult={lockResult}，Resource={lockResource}");
                }
            }
        }

        private static async Task ReleasePostProcessingLockAsync(SqlConnection conn, string lockResource)
        {
            if (conn == null || conn.State != ConnectionState.Open) return;

            using (var cmd = new SqlCommand("sp_releaseapplock", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 60;
                cmd.Parameters.AddWithValue("@Resource", lockResource);
                cmd.Parameters.AddWithValue("@LockOwner", "Session");
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static bool IsDeadlock(SqlException ex)
        {
            return ex != null && ex.Errors.Cast<SqlError>().Any(error => error.Number == 1205);
        }

        private static string BuildPostProcessingLockResource(string flag, string sprocName)
        {
            string resource = $"DA_PostProcess:{sprocName}:{flag}";
            return resource.Length <= 255 ? resource : resource.Substring(0, 255);
        }

        /// <summary>
        /// 检查表是否存在，如果不存在则根据指定的列定义自动创建表。
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="columns">列定义集合</param>
        public async Task CreateTableIfNotExistsAsync(string tableName, IEnumerable<ColumnDefinition> columns, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            // 1. 检查表是否存在的 SQL 头
            var sqlBuilder = new System.Text.StringBuilder();
            sqlBuilder.AppendLine($"IF OBJECT_ID('[{tableName}]', 'U') IS NULL");
            sqlBuilder.AppendLine("BEGIN");
            sqlBuilder.AppendLine($"    CREATE TABLE [{tableName}] (");

            // 2. 拼接列
            var columnSqls = new List<string>();
            foreach (var col in columns)
            {
                string sqlType = GetSqlDataType(col.DataType, col.MaxLength);
                string nullConstraint = col.AllowNull ? "NULL" : "NOT NULL";
                string pkConstraint = col.IsPrimaryKey ? "PRIMARY KEY IDENTITY(1,1)" : ""; // 假设主键是自增的，根据需要调整

                columnSqls.Add($"        [{col.ColumnName}] {sqlType} {pkConstraint} {nullConstraint}");
            }

            sqlBuilder.AppendLine(string.Join(", \n", columnSqls));
            sqlBuilder.AppendLine("    )");
            sqlBuilder.AppendLine("END");

            string finalSql = sqlBuilder.ToString();

            // 3. 执行建表脚本
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using (var cmd = new SqlCommand(finalSql, conn))
                {
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
        }

        #region 辅助方法

        // 默认值生成逻辑
        private object GetDefaultValue(Type t)
        {
            if (t == typeof(string)) return string.Empty;
            if (t == typeof(Guid)) return Guid.NewGuid();
            if (t == typeof(DateTime)) return DateTime.Now;
            if (t == typeof(bool)) return false;
            if (t.IsValueType) return Activator.CreateInstance(t); // 处理所有数值类型
            return DBNull.Value;
        }

        /// <summary>
        /// 将 C# 类型映射为 SQL Server 数据类型
        /// </summary>
        private string GetSqlDataType(Type type, int? maxLength = null)
        {
            // 如果是 Nullable<T>，获取底层类型
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            if (underlyingType == typeof(int)) return "INT";
            if (underlyingType == typeof(long)) return "BIGINT";
            if (underlyingType == typeof(short)) return "SMALLINT";
            if (underlyingType == typeof(byte)) return "TINYINT";
            if (underlyingType == typeof(decimal)) return "DECIMAL(18, 4)";
            if (underlyingType == typeof(double)) return "FLOAT";
            if (underlyingType == typeof(float)) return "REAL";
            if (underlyingType == typeof(bool)) return "BIT";
            if (underlyingType == typeof(DateTime)) return "DATETIME2";
            if (underlyingType == typeof(Guid)) return "UNIQUEIDENTIFIER";

            if (underlyingType == typeof(string))
            {
                if (maxLength.HasValue && maxLength.Value > 0 && maxLength.Value <= 8000)
                    return $"NVARCHAR({maxLength.Value})";
                return "NVARCHAR(MAX)";
            }

            // 默认回退类型
            return "NVARCHAR(MAX)";
        }
        #endregion
    }
}
