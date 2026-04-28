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
using Learun.Application.WebApi.Modules.DT_DataAcquisitionSystem.Application.DTOs;

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
                FROM [DGMES].[dbo].[DA_AcquisitionLog]
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
                ([TaskId], [StartTime], [EndTime], [Status], [TotalConfigs], [SuccessCount], [FailureCount], [ProcessedCount], [Progress], [Message])
                OUTPUT INSERTED.[Id]
                VALUES
                (@TaskId, @StartTime, @EndTime, @Status, @TotalConfigs, @SuccessCount, @FailureCount, @ProcessedCount, @Progress, @Message);";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@TaskId", SqlDbType.Int).Value = entry.TaskId;
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
                ([TaskLogId], [ConfigId], [FileName], [StartRow], [ProcessedRows], [StartTime], [EndTime], [Status], [ErrorMessage])
                OUTPUT INSERTED.[Id]
                VALUES
                (@TaskLogId, @ConfigId, @FileName, @StartRow, @ProcessedRows, @StartTime, @EndTime, @Status, @ErrorMessage);";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    // 注意这里 TaskLogId 改为了 NVarChar，以匹配实体中的 string
                    cmd.Parameters.Add("@TaskLogId", SqlDbType.NVarChar).Value = (object)entry.TaskLogId ?? DBNull.Value;
                    cmd.Parameters.Add("@ConfigId", SqlDbType.Int).Value = entry.ConfigId;
                    cmd.Parameters.Add("@FileName", SqlDbType.NVarChar, 500).Value = (object)entry.FileName ?? DBNull.Value;
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

        public async Task<List<AcquisitionTaskLogEntry>> GetTaskLogsAsync(int pageNo, int pageSize, string status = null, DateTime? startTime = null, DateTime? endTime = null, int? taskId = null, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT 
                    CAST([Id] AS NVARCHAR(50)) AS [Id],
                    [TaskId],
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

        /// <summary>
        /// Dashboard 按时间范围读取任务总日志。
        /// </summary>
        public async Task<List<DashboardTaskLogDto>> GetDashboardTaskLogsAsync(DateTime startTime, DateTime endTime, int? limit = null)
        {
            if (endTime <= startTime)
            {
                return new List<DashboardTaskLogDto>();
            }

            const string sql = @"
                SELECT
                    [StartTime],
                    CAST([TaskId] AS NVARCHAR(50)) AS [TaskId],
                    [Status],
                    [Message],
                    [ProcessedCount],
                    [SuccessCount],
                    [FailureCount]
                FROM [dbo].[DA_AcquisitionTaskLog]
                WHERE [StartTime] >= @StartTime
                  AND [StartTime] < @EndTime
                ORDER BY [StartTime] DESC, [Id] DESC
                OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY;";

            int take = (limit.HasValue && limit.Value > 0) ? limit.Value : 1000000;

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);

                var result = await conn.QueryAsync<DashboardTaskLogDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            StartTime = startTime,
                            EndTime = endTime,
                            Take = take
                        }))
                    .ConfigureAwait(false);

                return result?.ToList() ?? new List<DashboardTaskLogDto>();
            }
        }
    }
}
