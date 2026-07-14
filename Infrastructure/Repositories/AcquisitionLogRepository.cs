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

        public async Task<int> GetLastProcessedRowByConfigIdAsync(int configId, string fileName, CancellationToken ct = default)
        {
            // 逻辑：寻找【该配置下】【同一个日期】最后一次执行成功的记录，获取其已处理到的位置
            // 这里 ProcessedRows 记录的是该次任务处理掉的总行数
            // 如果想获取下一次的起点，通常是 StartRow + ProcessedRows
            const string sql = @"
                SELECT TOP 1 ([StartRow] + [ProcessedRows]) as NextStartRow
                FROM [dbo].[DA_AcquisitionLog]
                WHERE [ConfigId] = @ConfigId AND [FileName] = @FileName AND [Status] = 'Success'
                ORDER BY [Id] DESC";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@ConfigId", SqlDbType.Int).Value = configId;
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
                ([TaskLogId], [ConfigId], [FileName], [FullFilePath], [StartRow], [ProcessedRows], [StartTime], [EndTime], [Status], [ErrorMessage])
                OUTPUT INSERTED.[Id]
                VALUES
                (@TaskLogId, @ConfigId, @FileName, @FullFilePath, @StartRow, @ProcessedRows, @StartTime, @EndTime, @Status, @ErrorMessage);";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    // 注意这里 TaskLogId 改为了 NVarChar，以匹配实体中的 string
                    cmd.Parameters.Add("@TaskLogId", SqlDbType.NVarChar).Value = (object)entry.TaskLogId ?? DBNull.Value;
                    cmd.Parameters.Add("@ConfigId", SqlDbType.Int).Value = entry.ConfigId;
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

        public async Task<List<AcquisitionLogEntry>> GetLogsByTaskLogIdAsync(string taskLogId, int pageNo, int pageSize, string status = null, CancellationToken ct = default)
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
                  AND (
                    @Status IS NULL
                    OR (@Status = 'Warning' AND ISNULL([ErrorMessage], '') LIKE @MissingFilePattern)
                    OR (@Status = 'Failed' AND [Status] = 'Failed' AND ISNULL([ErrorMessage], '') NOT LIKE @MissingFilePattern)
                    OR (@Status NOT IN ('Warning', 'Failed') AND [Status] = @Status)
                  )
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
                            MissingFilePattern = "%\u6587\u4ef6\u672a\u627e\u5230%",
                            Offset = offset,
                            PageSize = safePageSize
                        },
                        cancellationToken: ct
                    )).ConfigureAwait(false);

                return result?.ToList() ?? new List<AcquisitionLogEntry>();
            }
        }

        public async Task<int> GetLogsCountByTaskLogIdAsync(string taskLogId, string status = null, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM [dbo].[DA_AcquisitionLog]
                WHERE [TaskLogId] = @TaskLogId
                  AND (
                    @Status IS NULL
                    OR (@Status = 'Warning' AND ISNULL([ErrorMessage], '') LIKE @MissingFilePattern)
                    OR (@Status = 'Failed' AND [Status] = 'Failed' AND ISNULL([ErrorMessage], '') NOT LIKE @MissingFilePattern)
                    OR (@Status NOT IN ('Warning', 'Failed') AND [Status] = @Status)
                  );";

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
                            MissingFilePattern = "%\u6587\u4ef6\u672a\u627e\u5230%"
                        },
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
                  AND (@EndTime IS NULL OR [StartTime] <= @EndTime);";

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
