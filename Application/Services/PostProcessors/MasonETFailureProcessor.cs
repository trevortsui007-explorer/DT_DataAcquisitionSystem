using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Application.Services.PostProcessors
{
    public class MasonETFailureProcessor : IPostProcessor
    {
        private readonly IDataService _dataService;
        private readonly string _connectionString;

        // 对应数据库中 ServiceName 字段的值
        public string ProcessorName => "MasonETFailureService";

        // 正则表达式：解析 Error 字段中的坐标信息和操作类型
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

        public async Task ExecuteAsync(string flag, AcquisitionConfig config, CancellationToken ct)
        {
            // 1. 获取未处理的原始坏点数据
            var rawFailureList = await GetUnprocessedFailureDataAsync(config.TableName, ct);
            if (!rawFailureList.Any()) return;

            // 2. 解析坏点数据并转换为结构化列表
            var etFailureDataList = ProcessRawData(rawFailureList, out List<Guid> processedIds);

            if (etFailureDataList.Any())
            {
                // 3. 将结构化数据转换成 DataTable 并执行 BulkInsert
                DataTable schema = await _dataService.GetTableSchemaAsync(config.PostTableName);
                DataTable dataToInsert = _dataService.PopulateDataTable(etFailureDataList, schema);

                await _dataService.BulkInsertAsync(dataToInsert, config.PostTableName, ct);

                // 4. 更新已处理的坏点数据状态 (IsProcessed = 1)
                await UpdateStatusByIdsAsync(config.TableName, processedIds, ct);
            }

            // 5. 更新所有 PASS 数据的处理状态（批量更新）
            await UpdatePassDataStatusAsync(config.TableName, ct);
        }

        #region 内部逻辑方法

        /// <summary>
        /// 获取原始数据
        /// </summary>
        private async Task<List<RawFailureDto>> GetUnprocessedFailureDataAsync(string sourceTableName, CancellationToken ct)
        {
            string sql = $@"SELECT Id, date, badPointInfo 
                           FROM [{sourceTableName}] 
                           WHERE testResult = 'FAIL' AND IsProcessed = 0 AND badPointInfo IS NOT NULL";

            var list = new List<RawFailureDto>();
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await reader.ReadAsync(ct))
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

        /// <summary>
        /// 正则解析核心逻辑
        /// </summary>
        private List<Dictionary<string, object>> ProcessRawData(List<RawFailureDto> rawData, out List<Guid> idList)
        {
            var result = new List<Dictionary<string, object>>();
            idList = new List<Guid>();

            foreach (var item in rawData)
            {
                var processedInfo = Regex.Replace(item.BadPointInfo, @"^Error:\s+", string.Empty);
                var matches = CoordinateRegex.Matches(processedInfo);

                foreach (Match match in matches)
                {
                    if (!match.Success) continue;

                    var row = new Dictionary<string, object>
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
                    };
                    result.Add(row);
                    idList.Add(item.Id);
                }
            }
            return result;
        }

        /// <summary>
        /// 批量更新状态
        /// </summary>
        private async Task UpdateStatusByIdsAsync(string sourceTableName, List<Guid> ids, CancellationToken ct)
        {
            if (!ids.Any()) return;
            const int batchSize = 1000;
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                for (int i = 0; i < ids.Count; i += batchSize)
                {
                    var currentBatch = ids.Skip(i).Take(batchSize);
                    var idString = string.Join(",", currentBatch.Select(id => $"'{id}'"));
                    string sql = $"UPDATE [{sourceTableName}] SET [IsProcessed] = 1 WHERE [Id] IN ({idString})";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
            }
        }

        /// <summary>
        /// 批量更新 PASS 数据状态
        /// </summary>
        private async Task UpdatePassDataStatusAsync(string sourceTableName, CancellationToken ct)
        {
            int batchSize = 50000;
            int rowsUpdated;
            string sql = $@"
                WITH BatchToUpdate AS (
                    SELECT TOP ({batchSize}) [ID] FROM [{sourceTableName}]
                    WHERE [IsProcessed] = 0 AND [testResult] = 'PASS'
                    ORDER BY [ID] ASC
                )
                UPDATE T SET [IsProcessed] = 1 
                FROM [{sourceTableName}] T 
                INNER JOIN BatchToUpdate B ON T.[ID] = B.[ID];";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct);
                do
                {
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        rowsUpdated = await cmd.ExecuteNonQueryAsync(ct);
                    }
                } while (rowsUpdated == batchSize);
            }
        }

        #endregion

        // 内部 DTO
        private class RawFailureDto
        {
            public Guid Id { get; set; }
            public DateTime Date { get; set; }
            public string BadPointInfo { get; set; }
        }
    }
}