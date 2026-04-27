using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace DT_DataAcquisitionSystem.Application.Services
{
    /// <summary>
    /// 任务可视编号生成器
    /// 编号规则：ACQ-{TriggerType}-{yyyyMMdd}-{D4}
    /// 例如：
    /// ACQ-MAN-20260426-0001
    /// ACQ-SCH-20260426-0002
    /// </summary>
    public class LogCodeGenerator : ILogCodeGenerator
    {
        private readonly string _connectionString;

        public LogCodeGenerator()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["BaseDb"].ConnectionString;
        }

        public LogCodeGenerator(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<string> GenerateTaskCodeAsync(string triggerType, CancellationToken ct = default)
        {
            string normalizedTriggerType = NormalizeTriggerType(triggerType);
            string seedDate = DateTime.Now.ToString("yyyyMMdd");

            int nextValue;
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                nextValue = await conn.ExecuteScalarAsync<int>(
                    sql: "dbo.sp_GetNextTaskCodeSeed",
                    param: new
                    {
                        SeedDate = seedDate
                    },
                    commandType: CommandType.StoredProcedure
                ).ConfigureAwait(false);
            }

            return $"ACQ-{normalizedTriggerType}-{seedDate}-{nextValue:D4}";
        }

        /// <summary>
        /// 标准化触发类型，目前只允许 MAN / SCH
        /// </summary>
        private static string NormalizeTriggerType(string triggerType)
        {
            if (string.IsNullOrWhiteSpace(triggerType))
            {
                return "MAN";
            }

            string normalized = triggerType.Trim().ToUpperInvariant();

            if (normalized == "MAN" || normalized == "SCH")
            {
                return normalized;
            }

            throw new InvalidOperationException("生成任务编号失败：TriggerType 仅允许 MAN 或 SCH。");
        }
    }
}