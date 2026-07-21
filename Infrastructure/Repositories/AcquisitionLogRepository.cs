using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using Microsoft.Practices.Unity;

namespace DT_DataAcquisitionSystem.Infrastructure.Repositories
{
    public class AcquisitionLogRepository : IAcquisitionLogRepository
    {
        private readonly string _connectionString;

        [InjectionConstructor]
        public AcquisitionLogRepository()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["BaseDb"].ConnectionString;
        }

        public AcquisitionLogRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<int> GetLastProcessedRowByConfigIdAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default)
        {
            // 逻辑：寻找【该配置下】【同一个日期】最后一次执行成功的记录，获取其已处理到的位置
            // 这里 ProcessedRows 记录的是该次任务处理掉的总行数
            // 如果想获取下一次的起点，通常是 StartRow + ProcessedRows
            const string sql = @"
                SELECT TOP 1 ([StartRow] + [ProcessedRows]) as NextStartRow
                FROM [dbo].[DA_AcquisitionLog]
                WHERE [ConfigId] = @ConfigId
                  AND [BusinessDate] = @BusinessDate
                  AND [FileName] = @FileName
                  AND [Status] = 'Success'
                ORDER BY [EndTime] DESC";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@ConfigId", SqlDbType.Int).Value = configId;
                    cmd.Parameters.Add("@BusinessDate", SqlDbType.Date).Value = businessDate.Date;
                    cmd.Parameters.Add("@FileName", SqlDbType.NVarChar).Value = fileName;

                    var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);

                    // 如果没有记录，result 会是 null
                    if (result == null || result == DBNull.Value)
                    {
                        return 0;
                    }

                    return Convert.ToInt32(result);
                }
            }
        }

        public async Task<string> InsertAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default)
        {
            const string sql = @"
                INSERT INTO [dbo].[DA_AcquisitionTaskLog]
                ([TaskId],[TaskCode], [TriggerType], [StartTime], [EndTime], [Status], [TotalConfigs], [SuccessCount], [FailureCount], [ProcessedCount], [Progress], [Message])
                OUTPUT INSERTED.[Id]
                VALUES
                (@TaskId,@TaskCode, @TriggerType, @StartTime, @EndTime, @Status, @TotalConfigs, @SuccessCount, @FailureCount, @ProcessedCount, @Progress, @Message);";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@TaskId", SqlDbType.Int).Value = entry.TaskId;
                    cmd.Parameters.Add("@TaskCode", SqlDbType.VarChar, 50).Value = (object)entry.TaskCode ?? DBNull.Value;
                    cmd.Parameters.Add("@TriggerType", SqlDbType.VarChar, 10).Value = (object)entry.TriggerType ?? DBNull.Value;
                    cmd.Parameters.Add("@StartTime", SqlDbType.DateTime).Value = entry.StartTime;
                    cmd.Parameters.Add("@EndTime", SqlDbType.DateTime).Value = (object)entry.EndTime ?? DBNull.Value;
                    cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = (object)entry.Status ?? DBNull.Value;
                    cmd.Parameters.Add("@TotalConfigs", SqlDbType.Int).Value = entry.TotalConfigs;
                    cmd.Parameters.Add("@SuccessCount", SqlDbType.Int).Value = entry.SuccessCount;
                    cmd.Parameters.Add("@FailureCount", SqlDbType.Int).Value = entry.FailureCount;
                    cmd.Parameters.Add("@ProcessedCount", SqlDbType.Int).Value = entry.ProcessedCount;
                    cmd.Parameters.Add("@Progress", SqlDbType.Int).Value = entry.Progress;
                    cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 500).Value = (object)entry.Message ?? DBNull.Value;

                    var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);

                    if (result != null && result != DBNull.Value)
                    {
                        string newId = result.ToString();
                        entry.Id = newId; // 回填 ID 到实体
                        return newId;
                    }
                    return null;
                }
            }
        }

        public async Task<string> InsertAsync(AcquisitionLogEntry entry, CancellationToken ct = default)
        {
            const string sql = @"
                INSERT INTO [dbo].[DA_AcquisitionLog]
                ([TaskLogId], [ConfigId], [BusinessDate], [FileName], [FullFilePath], [StartRow], [ProcessedRows], [StartTime], [EndTime], [Status], [ErrorMessage])
                OUTPUT INSERTED.[Id]
                VALUES
                (@TaskLogId, @ConfigId, @BusinessDate, @FileName, @FullFilePath, @StartRow, @ProcessedRows, @StartTime, @EndTime, @Status, @ErrorMessage);";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    // 注意这里 TaskLogId 改为了 NVarChar，以匹配实体中的 string
                    cmd.Parameters.Add("@TaskLogId", SqlDbType.NVarChar).Value = (object)entry.TaskLogId ?? DBNull.Value;
                    cmd.Parameters.Add("@ConfigId", SqlDbType.Int).Value = entry.ConfigId;
                    cmd.Parameters.Add("@BusinessDate", SqlDbType.Date).Value = (object)entry.BusinessDate?.Date ?? DBNull.Value;
                    cmd.Parameters.Add("@FileName", SqlDbType.NVarChar, 500).Value = (object)entry.FileName ?? DBNull.Value;
                    cmd.Parameters.Add("@FullFilePath", SqlDbType.NVarChar).Value = (object)entry.FullFilePath ?? DBNull.Value;
                    cmd.Parameters.Add("@StartRow", SqlDbType.Int).Value = entry.StartRow;
                    cmd.Parameters.Add("@ProcessedRows", SqlDbType.Int).Value = entry.ProcessedRows;
                    cmd.Parameters.Add("@StartTime", SqlDbType.DateTime).Value = (object)entry.StartTime ?? DBNull.Value;
                    cmd.Parameters.Add("@EndTime", SqlDbType.DateTime).Value = (object)entry.EndTime ?? DBNull.Value;
                    cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = (object)entry.Status ?? DBNull.Value;
                    cmd.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar).Value = (object)entry.ErrorMessage ?? DBNull.Value;

                    var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);

                    if (result != null && result != DBNull.Value)
                    {
                        string newId = result.ToString();
                        entry.Id = newId; // 回填 ID 到实体
                        return newId;
                    }
                    return null;
                }
            }
        }

        // 更新总任务日志：通常用于任务完成时记录结束时间、状态和成功统计
        public async Task<bool> UpdateAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE [dbo].[DA_AcquisitionTaskLog]
                SET [EndTime] = @EndTime,
                    [Status] = @Status,
                    [SuccessCount] = @SuccessCount,
                    [FailureCount] = @FailureCount,
                    [ProcessedCount] = @ProcessedCount,
                    [Progress] = @Progress,
                    [Message] = @Message
                WHERE [Id] = @Id";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                int rows = await conn.ExecuteAsync(sql, new
                {
                    entry.EndTime,
                    entry.Status,
                    entry.SuccessCount,
                    entry.FailureCount,
                    entry.ProcessedCount,
                    entry.Progress,
                    entry.Message,
                    entry.Id
                });

                return rows > 0;
            }
        }

        // 更新运行中的进度
        public async Task<bool> UpdateProgressAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE [dbo].[DA_AcquisitionTaskLog]
                SET [Status] = @Status,
                    [SuccessCount] = @SuccessCount,
                    [FailureCount] = @FailureCount,
                    [ProcessedCount] = @ProcessedCount,
                    [Progress] = @Progress,
                    [Message] = @Message
                WHERE [Id] = @Id";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                int rows = await conn.ExecuteAsync(sql, new
                {
                    entry.Status,
                    entry.SuccessCount,
                    entry.FailureCount,
                    entry.ProcessedCount,
                    entry.Progress,
                    entry.Message,
                    entry.Id
                });

                return rows > 0;
            }
        }

        // 按 taskLogId 查询总任务日志
        public async Task<AcquisitionTaskLogEntry> GetTaskLogByIdAsync(string taskLogId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT TOP 1
                    CAST([Id] AS NVARCHAR(50)) AS [Id],
                    [TaskId],
                    [TaskCode],
                    [TriggerType],
                    [StartTime],
                    [EndTime],
                    [Status],
                    [TotalConfigs],
                    [SuccessCount],
                    [FailureCount],
                    [ProcessedCount],
                    [Progress],
                    [Message]
                FROM [dbo].[DA_AcquisitionTaskLog]
                WHERE [Id] = @TaskLogId";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                var result = await conn.QueryFirstOrDefaultAsync<AcquisitionTaskLogEntry>(
                    new CommandDefinition(
                        sql,
                        new { TaskLogId = taskLogId },
                        cancellationToken: ct
                    )).ConfigureAwait(false);

                return result;
            }
        }

        // 按 taskLogId 查询明细日志
        public async Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT
                    CAST([Id] AS NVARCHAR(50)) AS [Id],
                    CAST([TaskLogId] AS NVARCHAR(50)) AS [TaskLogId],
                    [ConfigId],
                    [FileName],
                    [FullFilePath],
                    [StartRow],
                    [ProcessedRows],
                    [StartTime],
                    [EndTime],
                    [Status],
                    [ErrorMessage]
                FROM [dbo].[DA_AcquisitionLog]
                WHERE [TaskLogId] = @TaskLogId
                ORDER BY [StartTime] DESC, [Id] DESC";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                var result = await conn.QueryAsync<AcquisitionLogEntry>(
                    new CommandDefinition(
                        sql,
                        new { TaskLogId = taskLogId },
                        cancellationToken: ct
                    )).ConfigureAwait(false);

                return result.ToList();
            }
        }

        public async Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, int pageNo, int pageSize, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            const string sql = @"
                WITH FilteredLogs AS
                (
                    SELECT
                        CAST([Id] AS NVARCHAR(50)) AS [Id],
                        CAST([TaskLogId] AS NVARCHAR(50)) AS [TaskLogId],
                        [ConfigId],
                        [FileName],
                        [FullFilePath],
                        [StartRow],
                        [ProcessedRows],
                        [StartTime],
                        [EndTime],
                        [Status],
                        [ErrorMessage],
                        CASE
                            WHEN [Status] = 'Success' THEN NULL
                            WHEN ISNULL([ErrorMessage], '') LIKE '%Post processing failed%' THEN 'PostProcessing'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%文件未找到%' OR ISNULL([ErrorMessage], '') LIKE N'%不存在%' OR ISNULL([ErrorMessage], '') LIKE N'%未找到可处理文件%' OR ISNULL([ErrorMessage], '') LIKE '%File not found%' THEN 'FileMissing'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%SMB 凭据%' OR ISNULL([ErrorMessage], '') LIKE N'%凭据%' OR ISNULL([ErrorMessage], '') LIKE N'%用户名%' OR ISNULL([ErrorMessage], '') LIKE N'%密码%' OR ISNULL([ErrorMessage], '') LIKE N'%登录失败%' OR ISNULL([ErrorMessage], '') LIKE N'%网络路径%' OR ISNULL([ErrorMessage], '') LIKE N'%FTP 连接%' THEN 'PathCredential'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%正由另一进程使用%' OR ISNULL([ErrorMessage], '') LIKE N'%被占用%' OR ISNULL([ErrorMessage], '') LIKE N'%拒绝访问%' OR ISNULL([ErrorMessage], '') LIKE '%Access denied%' THEN 'PermissionLocked'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%缺少列%' OR ISNULL([ErrorMessage], '') LIKE N'%表头%' OR ISNULL([ErrorMessage], '') LIKE N'%格式%' OR ISNULL([ErrorMessage], '') LIKE N'%模板%' OR ISNULL([ErrorMessage], '') LIKE '%Sheet%' THEN 'FormatHeader'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%解析%' OR ISNULL([ErrorMessage], '') LIKE N'%转换%' OR ISNULL([ErrorMessage], '') LIKE '%DateTime%' OR ISNULL([ErrorMessage], '') LIKE '%Int32%' OR ISNULL([ErrorMessage], '') LIKE '%Decimal%' OR ISNULL([ErrorMessage], '') LIKE N'%输入字符串的格式不正确%' THEN 'DataParsing'
                            WHEN ISNULL([ErrorMessage], '') LIKE '%SQL%' OR ISNULL([ErrorMessage], '') LIKE N'%数据库%' OR ISNULL([ErrorMessage], '') LIKE '%INSERT%' OR ISNULL([ErrorMessage], '') LIKE N'%存储过程%' OR ISNULL([ErrorMessage], '') LIKE N'%死锁%' OR ISNULL([ErrorMessage], '') LIKE N'%违反%' OR ISNULL([ErrorMessage], '') LIKE N'%截断%' THEN 'DatabaseInsert'
                            ELSE 'Unknown'
                        END AS [ErrorCategory]
                    FROM [dbo].[DA_AcquisitionLog]
                    WHERE [TaskLogId] = @TaskLogId
                )
                SELECT
                    [Id],
                    [TaskLogId],
                    [ConfigId],
                    [FileName],
                    [FullFilePath],
                    [StartRow],
                    [ProcessedRows],
                    [StartTime],
                    [EndTime],
                    [Status],
                    [ErrorMessage]
                FROM FilteredLogs
                WHERE 1 = 1
                  AND (
                    @Status IS NULL
                    OR (@Status = 'Warning' AND [ErrorCategory] = 'FileMissing')
                    OR (@Status = 'Failed' AND [Status] = 'Failed' AND ISNULL([ErrorCategory], '') <> 'FileMissing')
                    OR (@Status NOT IN ('Warning', 'Failed') AND [Status] = @Status)
                  )
                  AND (@ErrorCategory IS NULL OR [ErrorCategory] = @ErrorCategory)
                  AND (@HasProcessedRows = 0 OR ISNULL([ProcessedRows], 0) > 0)
                ORDER BY [StartTime] DESC, [Id] DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

            int safePageNo = pageNo <= 0 ? 1 : pageNo;
            int safePageSize = pageSize <= 0 ? 10 : pageSize;
            int offset = (safePageNo - 1) * safePageSize;

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                var result = await conn.QueryAsync<AcquisitionLogEntry>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            TaskLogId = taskLogId,
                            Status = NormalizeDetailStatus(status),
                            ErrorCategory = NormalizeErrorCategory(errorCategory),
                            HasProcessedRows = hasProcessedRows,
                            MissingFilePattern = "%\u6587\u4ef6\u672a\u627e\u5230%",
                            Offset = offset,
                            PageSize = safePageSize
                        },
                        cancellationToken: ct
                    )).ConfigureAwait(false);

                return result?.ToList() ?? new List<AcquisitionLogEntry>();
            }
        }

        public async Task<int> GetLogsCountByTaskLogIdAsync(string taskLogId, string status = null, string errorCategory = null, bool hasProcessedRows = false, CancellationToken ct = default)
        {
            const string sql = @"
                WITH FilteredLogs AS
                (
                    SELECT
                        [Status],
                        [ErrorMessage],
                        [ProcessedRows],
                        CASE
                            WHEN [Status] = 'Success' THEN NULL
                            WHEN ISNULL([ErrorMessage], '') LIKE '%Post processing failed%' THEN 'PostProcessing'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%文件未找到%' OR ISNULL([ErrorMessage], '') LIKE N'%不存在%' OR ISNULL([ErrorMessage], '') LIKE N'%未找到可处理文件%' OR ISNULL([ErrorMessage], '') LIKE '%File not found%' THEN 'FileMissing'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%SMB 凭据%' OR ISNULL([ErrorMessage], '') LIKE N'%凭据%' OR ISNULL([ErrorMessage], '') LIKE N'%用户名%' OR ISNULL([ErrorMessage], '') LIKE N'%密码%' OR ISNULL([ErrorMessage], '') LIKE N'%登录失败%' OR ISNULL([ErrorMessage], '') LIKE N'%网络路径%' OR ISNULL([ErrorMessage], '') LIKE N'%FTP 连接%' THEN 'PathCredential'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%正由另一进程使用%' OR ISNULL([ErrorMessage], '') LIKE N'%被占用%' OR ISNULL([ErrorMessage], '') LIKE N'%拒绝访问%' OR ISNULL([ErrorMessage], '') LIKE '%Access denied%' THEN 'PermissionLocked'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%缺少列%' OR ISNULL([ErrorMessage], '') LIKE N'%表头%' OR ISNULL([ErrorMessage], '') LIKE N'%格式%' OR ISNULL([ErrorMessage], '') LIKE N'%模板%' OR ISNULL([ErrorMessage], '') LIKE '%Sheet%' THEN 'FormatHeader'
                            WHEN ISNULL([ErrorMessage], '') LIKE N'%解析%' OR ISNULL([ErrorMessage], '') LIKE N'%转换%' OR ISNULL([ErrorMessage], '') LIKE '%DateTime%' OR ISNULL([ErrorMessage], '') LIKE '%Int32%' OR ISNULL([ErrorMessage], '') LIKE '%Decimal%' OR ISNULL([ErrorMessage], '') LIKE N'%输入字符串的格式不正确%' THEN 'DataParsing'
                            WHEN ISNULL([ErrorMessage], '') LIKE '%SQL%' OR ISNULL([ErrorMessage], '') LIKE N'%数据库%' OR ISNULL([ErrorMessage], '') LIKE '%INSERT%' OR ISNULL([ErrorMessage], '') LIKE N'%存储过程%' OR ISNULL([ErrorMessage], '') LIKE N'%死锁%' OR ISNULL([ErrorMessage], '') LIKE N'%违反%' OR ISNULL([ErrorMessage], '') LIKE N'%截断%' THEN 'DatabaseInsert'
                            ELSE 'Unknown'
                        END AS [ErrorCategory]
                    FROM [dbo].[DA_AcquisitionLog]
                    WHERE [TaskLogId] = @TaskLogId
                )
                SELECT COUNT(1)
                FROM FilteredLogs
                WHERE 1 = 1
                  AND (
                    @Status IS NULL
                    OR (@Status = 'Warning' AND [ErrorCategory] = 'FileMissing')
                    OR (@Status = 'Failed' AND [Status] = 'Failed' AND ISNULL([ErrorCategory], '') <> 'FileMissing')
                    OR (@Status NOT IN ('Warning', 'Failed') AND [Status] = @Status)
                  )
                  AND (@ErrorCategory IS NULL OR [ErrorCategory] = @ErrorCategory)
                  AND (@HasProcessedRows = 0 OR ISNULL([ProcessedRows], 0) > 0);";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                return await conn.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            TaskLogId = taskLogId,
                            Status = NormalizeDetailStatus(status),
                            ErrorCategory = NormalizeErrorCategory(errorCategory),
                            HasProcessedRows = hasProcessedRows,
                            MissingFilePattern = "%\u6587\u4ef6\u672a\u627e\u5230%"
                        },
                        cancellationToken: ct
                    )).ConfigureAwait(false);
            }
        }

        public async Task<int> GetLogsProcessedRowsByTaskLogIdAsync(string taskLogId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT ISNULL(SUM([ProcessedRows]), 0)
                FROM [dbo].[DA_AcquisitionLog]
                WHERE [TaskLogId] = @TaskLogId;";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                return await conn.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        sql,
                        new { TaskLogId = taskLogId },
                        cancellationToken: ct
                    )).ConfigureAwait(false);
            }
        }

        public async Task<List<AcquisitionTaskLogEntry>> GetTaskLogsAsync(int pageNo, int pageSize, string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT 
                    CAST([Id] AS NVARCHAR(50)) AS [Id],
                    [TaskId],
                    [TaskCode],
                    [TriggerType],
                    [StartTime],
                    [EndTime],
                    [Status],
                    [TotalConfigs],
                    [SuccessCount],
                    [FailureCount],
                    [ProcessedCount],
                    [Progress],
                    [Message]
                FROM [dbo].[DA_AcquisitionTaskLog]
                WHERE (@Status IS NULL OR [Status] = @Status)
                  AND (@TaskId IS NULL OR [TaskId] = @TaskId)
                  AND (@StartTime IS NULL OR [StartTime] >= @StartTime)
                  AND (@EndTime IS NULL OR [StartTime] <= @EndTime)
                  AND ISNULL([TriggerType], '') <> 'TST'
                ORDER BY [StartTime] DESC, [Id] DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

            int safePageNo = pageNo <= 0 ? 1 : pageNo;
            int safePageSize = pageSize <= 0 ? 20 : pageSize;
            int offset = (safePageNo - 1) * safePageSize;

            using (var conn = new SqlConnection(_connectionString))
            {
                var result = await conn.QueryAsync<AcquisitionTaskLogEntry>(sql, new
                {
                    Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
                    TaskId = taskId,
                    StartTime = startTime,
                    EndTime = endTime,
                    Offset = offset,
                    PageSize = safePageSize
                });

                return result?.ToList() ?? new List<AcquisitionTaskLogEntry>();
            }
        }

        public async Task<int> GetTaskLogsCountAsync(string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM [dbo].[DA_AcquisitionTaskLog]
                WHERE (@Status IS NULL OR [Status] = @Status)
                  AND (@TaskId IS NULL OR [TaskId] = @TaskId)
                  AND (@StartTime IS NULL OR [StartTime] >= @StartTime)
                  AND (@EndTime IS NULL OR [StartTime] <= @EndTime)
                  AND ISNULL([TriggerType], '') <> 'TST';";

            using (var conn = new SqlConnection(_connectionString))
            {
                return await conn.ExecuteScalarAsync<int>(sql, new
                {
                    Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
                    TaskId = taskId,
                    StartTime = startTime,
                    EndTime = endTime
                });
            }
        }

        public async Task<Dictionary<string, int>> GetTaskLogWarningCountsAsync(IEnumerable<string> taskLogIds, CancellationToken ct = default)
        {
            var ids = (taskLogIds ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToArray();

            if (ids.Length == 0)
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            const string sql = @"
                SELECT
                    CAST([TaskLogId] AS NVARCHAR(50)) AS [TaskLogId],
                    COUNT(1) AS [WarningCount]
                FROM [dbo].[DA_AcquisitionLog]
                WHERE CAST([TaskLogId] AS NVARCHAR(50)) IN @TaskLogIds
                  AND [Status] = 'Failed'
                  AND (
                    ISNULL([ErrorMessage], '') LIKE @FileMissingPattern1
                    OR ISNULL([ErrorMessage], '') LIKE @FileMissingPattern2
                    OR ISNULL([ErrorMessage], '') LIKE @FileMissingPattern3
                    OR ISNULL([ErrorMessage], '') LIKE @FileMissingPattern4
                  )
                GROUP BY CAST([TaskLogId] AS NVARCHAR(50));";

            using (var conn = new SqlConnection(_connectionString))
            {
                var rows = await conn.QueryAsync<(string TaskLogId, int WarningCount)>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            TaskLogIds = ids,
                            FileMissingPattern1 = "%\u6587\u4ef6\u672a\u627e\u5230%",
                            FileMissingPattern2 = "%\u4e0d\u5b58\u5728%",
                            FileMissingPattern3 = "%\u672a\u627e\u5230\u53ef\u5904\u7406\u6587\u4ef6%",
                            FileMissingPattern4 = "%File not found%"
                        },
                        cancellationToken: ct
                    )).ConfigureAwait(false);

                return rows.ToDictionary(
                    x => x.TaskLogId,
                    x => x.WarningCount,
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string NormalizeDetailStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return null;

            string normalized = status.Trim();
            if (normalized.Equals("All", StringComparison.OrdinalIgnoreCase)) return null;
            if (normalized.Equals("Warning", StringComparison.OrdinalIgnoreCase)) return "Warning";
            if (normalized.Equals("Failed", StringComparison.OrdinalIgnoreCase)) return "Failed";
            if (normalized.Equals("Success", StringComparison.OrdinalIgnoreCase)) return "Success";
            if (normalized.Equals("Running", StringComparison.OrdinalIgnoreCase)) return "Running";

            return normalized;
        }

        private static string NormalizeErrorCategory(string errorCategory)
        {
            if (string.IsNullOrWhiteSpace(errorCategory)) return null;

            string normalized = errorCategory.Trim();
            if (normalized.Equals("All", StringComparison.OrdinalIgnoreCase)) return null;

            string[] supported =
            {
                "FileMissing",
                "PathCredential",
                "PermissionLocked",
                "FormatHeader",
                "DataParsing",
                "DatabaseInsert",
                "PostProcessing",
                "Unknown"
            };

            return supported.FirstOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static object BuildDetailQueryParams(string taskLogId, string status, string errorCategory, int? offset = null, int? pageSize = null)
        {
            return new
            {
                TaskLogId = taskLogId,
                Status = NormalizeDetailStatus(status),
                ErrorCategory = NormalizeErrorCategory(errorCategory),
                MissingFilePattern = "%\u6587\u4ef6\u672a\u627e\u5230%",
                FileMissingPattern1 = "%\u6587\u4ef6\u672a\u627e\u5230%",
                FileMissingPattern2 = "%\u4e0d\u5b58\u5728%",
                FileMissingPattern3 = "%\u672a\u627e\u5230\u53ef\u5904\u7406\u6587\u4ef6%",
                PathCredentialPattern1 = "%SMB \u51ed\u636e%",
                PathCredentialPattern2 = "%\u51ed\u636e%",
                PathCredentialPattern3 = "%\u7528\u6237\u540d%",
                PathCredentialPattern4 = "%\u5bc6\u7801%",
                PathCredentialPattern5 = "%\u767b\u5f55\u5931\u8d25%",
                PathCredentialPattern6 = "%\u7f51\u7edc\u8def\u5f84%",
                PathCredentialPattern7 = "%FTP \u8fde\u63a5%",
                PermissionLockedPattern1 = "%\u6b63\u7531\u53e6\u4e00\u8fdb\u7a0b\u4f7f\u7528%",
                PermissionLockedPattern2 = "%\u88ab\u5360\u7528%",
                PermissionLockedPattern3 = "%\u62d2\u7edd\u8bbf\u95ee%",
                FormatHeaderPattern1 = "%\u7f3a\u5c11\u5217%",
                FormatHeaderPattern2 = "%\u8868\u5934%",
                FormatHeaderPattern3 = "%\u683c\u5f0f%",
                FormatHeaderPattern4 = "%\u6a21\u677f%",
                DataParsingPattern1 = "%\u89e3\u6790%",
                DataParsingPattern2 = "%\u8f6c\u6362%",
                DataParsingPattern3 = "%\u8f93\u5165\u5b57\u7b26\u4e32\u7684\u683c\u5f0f\u4e0d\u6b63\u786e%",
                DatabaseInsertPattern1 = "%\u6570\u636e\u5e93%",
                DatabaseInsertPattern2 = "%\u5b58\u50a8\u8fc7\u7a0b%",
                DatabaseInsertPattern3 = "%\u6b7b\u9501%",
                DatabaseInsertPattern4 = "%\u8fdd\u53cd%",
                DatabaseInsertPattern5 = "%\u622a\u65ad%",
                Offset = offset ?? 0,
                PageSize = pageSize ?? 10
            };
        }
    }

    public class AcquisitionFileStateRepository : IAcquisitionFileStateRepository
    {
        private readonly string _connectionString;

        [InjectionConstructor]
        public AcquisitionFileStateRepository()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["BaseDb"].ConnectionString;
        }

        public AcquisitionFileStateRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<AcquisitionFileState> GetAsync(int configId, DateTime businessDate, string fileName, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT TOP 1
                    [Id],
                    [ConfigId],
                    [BusinessDate],
                    [FileName],
                    [FullPath],
                    [DataRowCount],
                    [LastStartRow],
                    [LastProcessedRows],
                    [LastTaskLogId],
                    [LastStatus],
                    [LastUpdateSource],
                    [IsSealed],
                    [SealTime],
                    [LastScanTime],
                    [LastWriteTime],
                    [LastWriteTimeUtc],
                    [FileSize],
                    [CreateTime],
                    [UpdateTime]
                FROM [dbo].[DA_AcquisitionFileState]
                WHERE [ConfigId] = @ConfigId
                  AND [BusinessDate] = @BusinessDate
                  AND [FileName] = @FileName;";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                return await conn.QueryFirstOrDefaultAsync<AcquisitionFileState>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            ConfigId = configId,
                            BusinessDate = businessDate.Date,
                            FileName = fileName
                        },
                        cancellationToken: ct
                    )).ConfigureAwait(false);
            }
        }

        public async Task<List<AcquisitionFileState>> GetByConfigAndDateRangeAsync(int configId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT
                    [Id],
                    [ConfigId],
                    [BusinessDate],
                    [FileName],
                    [FullPath],
                    [DataRowCount],
                    [LastStartRow],
                    [LastProcessedRows],
                    [LastTaskLogId],
                    [LastStatus],
                    [LastUpdateSource],
                    [IsSealed],
                    [SealTime],
                    [LastScanTime],
                    [LastWriteTime],
                    [LastWriteTimeUtc],
                    [FileSize],
                    [CreateTime],
                    [UpdateTime]
                FROM [dbo].[DA_AcquisitionFileState]
                WHERE [ConfigId] = @ConfigId
                  AND [BusinessDate] >= @StartDate
                  AND [BusinessDate] <= @EndDate;";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                var result = await conn.QueryAsync<AcquisitionFileState>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            ConfigId = configId,
                            StartDate = startDate.Date,
                            EndDate = endDate.Date
                        },
                        cancellationToken: ct
                    )).ConfigureAwait(false);

                return result?.ToList() ?? new List<AcquisitionFileState>();
            }
        }

        public async Task<bool> UpsertSuccessAsync(AcquisitionFileState state, bool allowSealedUpdate, CancellationToken ct = default)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            const string sql = @"
                MERGE [dbo].[DA_AcquisitionFileState] WITH (HOLDLOCK) AS target
                USING (
                    SELECT
                        @ConfigId AS [ConfigId],
                        @BusinessDate AS [BusinessDate],
                        @FileName AS [FileName],
                        @FullPath AS [FullPath],
                        @DataRowCount AS [DataRowCount],
                        @LastStartRow AS [LastStartRow],
                        @LastProcessedRows AS [LastProcessedRows],
                        @LastTaskLogId AS [LastTaskLogId],
                        @LastStatus AS [LastStatus],
                        @LastUpdateSource AS [LastUpdateSource],
                        @LastWriteTime AS [LastWriteTime],
                        @LastWriteTimeUtc AS [LastWriteTimeUtc],
                        @FileSize AS [FileSize],
                        @Now AS [Now]
                ) AS source
                ON target.[ConfigId] = source.[ConfigId]
                   AND target.[BusinessDate] = source.[BusinessDate]
                   AND target.[FileName] = source.[FileName]
                WHEN MATCHED AND (target.[IsSealed] = 0 OR @AllowSealedUpdate = 1) THEN
                    UPDATE SET
                        [FullPath] = source.[FullPath],
                        [DataRowCount] = CASE
                            WHEN target.[DataRowCount] > source.[DataRowCount] THEN target.[DataRowCount]
                            ELSE source.[DataRowCount]
                        END,
                        [LastStartRow] = source.[LastStartRow],
                        [LastProcessedRows] = source.[LastProcessedRows],
                        [LastTaskLogId] = source.[LastTaskLogId],
                        [LastStatus] = source.[LastStatus],
                        [LastUpdateSource] = source.[LastUpdateSource],
                        [LastWriteTime] = source.[LastWriteTime],
                        [LastWriteTimeUtc] = source.[LastWriteTimeUtc],
                        [FileSize] = source.[FileSize],
                        [LastScanTime] = source.[Now],
                        [UpdateTime] = source.[Now]
                WHEN NOT MATCHED THEN
                    INSERT (
                        [ConfigId],
                        [BusinessDate],
                        [FileName],
                        [FullPath],
                        [DataRowCount],
                        [LastStartRow],
                        [LastProcessedRows],
                        [LastTaskLogId],
                        [LastStatus],
                        [LastUpdateSource],
                        [LastWriteTime],
                        [LastWriteTimeUtc],
                        [FileSize],
                        [IsSealed],
                        [LastScanTime],
                        [CreateTime],
                        [UpdateTime]
                    )
                    VALUES (
                        source.[ConfigId],
                        source.[BusinessDate],
                        source.[FileName],
                        source.[FullPath],
                        source.[DataRowCount],
                        source.[LastStartRow],
                        source.[LastProcessedRows],
                        source.[LastTaskLogId],
                        source.[LastStatus],
                        source.[LastUpdateSource],
                        source.[LastWriteTime],
                        source.[LastWriteTimeUtc],
                        source.[FileSize],
                        0,
                        source.[Now],
                        source.[Now],
                        source.[Now]
                    );";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                int rows = await conn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            state.ConfigId,
                            BusinessDate = state.BusinessDate.Date,
                            state.FileName,
                            state.FullPath,
                            state.DataRowCount,
                            state.LastStartRow,
                            state.LastProcessedRows,
                            state.LastTaskLogId,
                            state.LastStatus,
                            state.LastUpdateSource,
                            state.LastWriteTime,
                            state.LastWriteTimeUtc,
                            state.FileSize,
                            AllowSealedUpdate = allowSealedUpdate ? 1 : 0,
                            Now = DateTime.Now
                        },
                        cancellationToken: ct
                    )).ConfigureAwait(false);

                return rows > 0;
            }
        }

        public async Task<int> SealByTaskLogAsync(string taskLogId, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE [dbo].[DA_AcquisitionFileState]
                SET [IsSealed] = 1,
                    [SealTime] = CASE WHEN [SealTime] IS NULL THEN @Now ELSE [SealTime] END,
                    [UpdateTime] = @Now
                WHERE [LastTaskLogId] = @TaskLogId
                  AND [LastStatus] = 'Success'
                  AND [IsSealed] = 0;";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                return await conn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            TaskLogId = taskLogId,
                            Now = DateTime.Now
                        },
                        cancellationToken: ct
                    )).ConfigureAwait(false);
            }
        }

    }
}
