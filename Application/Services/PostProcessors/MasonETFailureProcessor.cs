using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Application.Services.PostProcessors
{
    public class MasonETFailureProcessor : IPostProcessor
    {
        private readonly IDataService _dataService;
        private readonly string _connectionString;

        public string ProcessorName => "MasonETFailureService";

        private static readonly Regex CoordinateRegex = new Regex(
            @"(?<Error>[+\-]{1,2}|\+\s+\+|\-\s+\-)\s*"
            + @"(?<Side1>[TB])\s*X(?<X1>\d+)\s*Y(?<Y1>\d+)"
            + @"(?:\s*P(?<P1>[-+]?\d+)\s*M(?<M1>[-+]?\d+))?"
            + @"(?:\s*(?<Error2>[+\-]{1,2}|\+\s+\+|\-\s+\-)?\s*"
            + @"(?<Side2>[TB])\s*X(?<X2>\d+)\s*Y(?<Y2>\d+)"
            + @"(?:\s*P(?<P2>[-+]?\d+)\s*M(?<M2>[-+]?\d+))?"
            + @")?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        public MasonETFailureProcessor(IDataService dataService)
        {
            _dataService = dataService;
            _connectionString = ConfigurationManager.ConnectionStrings["BaseDb"].ConnectionString;
        }

        public Task ExecuteAsync(string flag, AcquisitionConfig config, CancellationToken ct)
        {
            return ExecuteAsync(new PostProcessingContext
            {
                Config = config,
                SourceTableName = config?.TableName,
                PostTableName = config?.PostTableName,
                Rows = new List<PostProcessingRowKey>()
            }, ct);
        }

        public async Task ExecuteAsync(PostProcessingContext context, CancellationToken ct)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Config == null) throw new ArgumentNullException(nameof(context.Config));

            var ids = (context.Rows ?? new List<PostProcessingRowKey>())
                .Where(x => x != null && x.Id != Guid.Empty)
                .Select(x => x.Id)
                .Distinct()
                .ToList();

            if (!ids.Any()) return;

            string sourceTableName = string.IsNullOrWhiteSpace(context.SourceTableName)
                ? context.Config.TableName
                : context.SourceTableName;
            string postTableName = string.IsNullOrWhiteSpace(context.PostTableName)
                ? context.Config.PostTableName
                : context.PostTableName;

            if (string.IsNullOrWhiteSpace(sourceTableName))
                throw new InvalidOperationException("MasonETFailureService 后处理缺少源表名。");
            if (string.IsNullOrWhiteSpace(postTableName))
                throw new InvalidOperationException("MasonETFailureService 后处理缺少坏点明细目标表名。");

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                await CreateTempIdTableAsync(conn, ct).ConfigureAwait(false);
                await BulkCopyIdsAsync(conn, ids, ct).ConfigureAwait(false);

                var rawFailureList = await GetUnprocessedFailureDataAsync(conn, sourceTableName, ct).ConfigureAwait(false);
                var failureRows = ProcessRawData(rawFailureList);
                DataTable failureDataTable = null;

                if (failureRows.Any())
                {
                    DataTable schema = await _dataService.GetTableSchemaAsync(postTableName).ConfigureAwait(false);
                    failureDataTable = _dataService.PopulateDataTable(failureRows, schema);
                }

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        await DeleteExistingFailureRowsAsync(conn, tx, postTableName, ct).ConfigureAwait(false);

                        if (failureDataTable != null && failureDataTable.Rows.Count > 0)
                        {
                            await BulkInsertAsync(conn, tx, failureDataTable, postTableName, ct).ConfigureAwait(false);
                        }

                        await MarkSourceRowsProcessedAsync(conn, tx, sourceTableName, ct).ConfigureAwait(false);
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

        private static async Task CreateTempIdTableAsync(SqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
                CREATE TABLE #PostProcessIds (
                    Id uniqueidentifier NOT NULL PRIMARY KEY
                );";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.CommandTimeout = 600;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        private static async Task BulkCopyIdsAsync(SqlConnection conn, List<Guid> ids, CancellationToken ct)
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(Guid));

            foreach (Guid id in ids)
            {
                table.Rows.Add(id);
            }

            using (var bulkCopy = new SqlBulkCopy(conn))
            {
                bulkCopy.DestinationTableName = "#PostProcessIds";
                bulkCopy.BatchSize = 1000;
                bulkCopy.BulkCopyTimeout = 600;
                bulkCopy.ColumnMappings.Add("Id", "Id");
                await bulkCopy.WriteToServerAsync(table, ct).ConfigureAwait(false);
            }
        }

        private static async Task<List<RawFailureDto>> GetUnprocessedFailureDataAsync(SqlConnection conn, string sourceTableName, CancellationToken ct)
        {
            string sql = $@"
                SELECT s.[Id], s.[date], s.[badPointInfo]
                FROM [{sourceTableName}] s
                INNER JOIN #PostProcessIds k ON s.[Id] = k.[Id]
                WHERE s.[testResult] = 'FAIL'
                  AND s.[IsProcessed] = 0
                  AND s.[badPointInfo] IS NOT NULL;";

            var list = new List<RawFailureDto>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.CommandTimeout = 600;
                using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        list.Add(new RawFailureDto
                        {
                            Id = reader.GetGuid(0),
                            Date = reader.GetDateTime(1),
                            BadPointInfo = reader.GetString(2)
                        });
                    }
                }
            }

            return list;
        }

        private static List<Dictionary<string, object>> ProcessRawData(List<RawFailureDto> rawData)
        {
            var result = new List<Dictionary<string, object>>();

            foreach (var item in rawData)
            {
                var processedInfo = Regex.Replace(item.BadPointInfo, @"^Error:\s+", string.Empty);
                var matches = CoordinateRegex.Matches(processedInfo);

                foreach (Match match in matches)
                {
                    if (!match.Success) continue;

                    result.Add(new Dictionary<string, object>
                    {
                        { "Id", item.Id },
                        { "Date", item.Date },
                        { "Error", match.Groups["Error"].Value.Trim() },
                        { "Side1", match.Groups["Side1"].Value },
                        { "X1", int.Parse(match.Groups["X1"].Value) },
                        { "Y1", int.Parse(match.Groups["Y1"].Value) },
                        { "SinglePointLeak1", match.Groups["P1"].Success ? $"P{match.Groups["P1"].Value} M{match.Groups["M1"].Value}" : null },
                        { "Side2", match.Groups["Side2"].Success ? match.Groups["Side2"].Value : null },
                        { "X2", match.Groups["X2"].Success ? int.Parse(match.Groups["X2"].Value) : (int?)null },
                        { "Y2", match.Groups["Y2"].Success ? int.Parse(match.Groups["Y2"].Value) : (int?)null },
                        { "SinglePointLeak2", match.Groups["P2"].Success ? $"P{match.Groups["P2"].Value} M{match.Groups["M2"].Value}" : null },
                        { "createdt", DateTime.Now }
                    });
                }
            }

            return result;
        }

        private static async Task DeleteExistingFailureRowsAsync(SqlConnection conn, SqlTransaction tx, string postTableName, CancellationToken ct)
        {
            string sql = $@"
                DELETE m
                FROM [{postTableName}] m
                INNER JOIN #PostProcessIds k ON m.[Id] = k.[Id];";

            using (var cmd = new SqlCommand(sql, conn, tx))
            {
                cmd.CommandTimeout = 600;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        private static async Task MarkSourceRowsProcessedAsync(SqlConnection conn, SqlTransaction tx, string sourceTableName, CancellationToken ct)
        {
            string sql = $@"
                UPDATE s
                SET s.[IsProcessed] = 1
                FROM [{sourceTableName}] s
                INNER JOIN #PostProcessIds k ON s.[Id] = k.[Id]
                WHERE s.[IsProcessed] = 0;";

            using (var cmd = new SqlCommand(sql, conn, tx))
            {
                cmd.CommandTimeout = 600;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        private static async Task BulkInsertAsync(SqlConnection conn, SqlTransaction tx, DataTable dataTable, string destinationTableName, CancellationToken ct)
        {
            if (dataTable == null || dataTable.Rows.Count == 0) return;

            using (var bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx))
            {
                bulkCopy.DestinationTableName = $"[{destinationTableName}]";
                bulkCopy.BatchSize = 50000;
                bulkCopy.BulkCopyTimeout = 600;

                foreach (DataColumn column in dataTable.Columns)
                {
                    if (column.AutoIncrement) continue;

                    bool isAllNull = true;
                    foreach (DataRow row in dataTable.Rows)
                    {
                        if (!row.IsNull(column))
                        {
                            isAllNull = false;
                            break;
                        }
                    }

                    if (isAllNull && !column.AllowDBNull) continue;

                    bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                }

                await bulkCopy.WriteToServerAsync(dataTable, ct).ConfigureAwait(false);
            }
        }

        private class RawFailureDto
        {
            public Guid Id { get; set; }
            public DateTime Date { get; set; }
            public string BadPointInfo { get; set; }
        }
    }
}
