using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Infrastructure.Repositories
{
    public class ReportExportDataRepository : IReportExportDataProvider
    {
        private readonly string _connectionString;

        public ReportExportDataRepository()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["BaseDb"]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                throw new InvalidOperationException("未配置 BaseDb 连接字符串。");
            }
        }

        public IList<ReportExportGroupDefinition> GetGroupDefinitions(IEnumerable<int> groupIds)
        {
            var ids = (groupIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!ids.Any()) return new List<ReportExportGroupDefinition>();

            bool hasExportColumn = HasColumn("DA_AcquisitionGroup", "ExportProcedureName");
            string exportColumn = hasExportColumn ? "ExportProcedureName" : "CAST('' AS NVARCHAR(200)) AS ExportProcedureName";
            string sql = $@"
SELECT Id AS GroupId, GroupName, {exportColumn}
FROM dbo.DA_AcquisitionGroup
WHERE Id IN ({string.Join(",", ids)})
ORDER BY Id ASC";

            var result = new List<ReportExportGroupDefinition>();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new ReportExportGroupDefinition
                        {
                            GroupId = Convert.ToInt32(reader["GroupId"]),
                            GroupName = reader["GroupName"]?.ToString(),
                            ExportProcedureName = reader["ExportProcedureName"]?.ToString()
                        });
                    }
                }
            }
            return result;
        }

        public IList<ReportDataSet> ExecuteGroupReport(int groupId, string procedureName, DateTime startTime, DateTime endTime)
        {
            if (string.IsNullOrWhiteSpace(procedureName))
            {
                throw new InvalidOperationException($"配置组 {groupId} 未配置导出存储过程。");
            }

            var dataSet = new DataSet();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(procedureName, conn))
            using (var adapter = new SqlDataAdapter(cmd))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 0;
                cmd.Parameters.AddWithValue("@StartTime", startTime);
                cmd.Parameters.AddWithValue("@EndTime", endTime);
                adapter.Fill(dataSet);
            }

            if (dataSet.Tables.Count == 0)
            {
                throw new InvalidOperationException($"存储过程 {procedureName} 未返回任何结果集。");
            }

            var result = new List<ReportDataSet>();
            var firstTable = dataSet.Tables[0];
            bool hasSheetMeta = firstTable.Columns.Contains("SheetName") && firstTable.Rows.Count == dataSet.Tables.Count - 1;

            if (hasSheetMeta)
            {
                for (int i = 1; i < dataSet.Tables.Count; i++)
                {
                    string sheetName = Convert.ToString(firstTable.Rows[i - 1]["SheetName"]);
                    result.Add(new ReportDataSet
                    {
                        SheetName = string.IsNullOrWhiteSpace(sheetName) ? $"Sheet{i}" : sheetName,
                        Data = dataSet.Tables[i]
                    });
                }
            }
            else
            {
                for (int i = 0; i < dataSet.Tables.Count; i++)
                {
                    result.Add(new ReportDataSet
                    {
                        SheetName = $"Sheet{i + 1}",
                        Data = dataSet.Tables[i]
                    });
                }
            }

            return result;
        }

        private bool HasColumn(string tableName, string columnName)
        {
            const string sql = "SELECT COL_LENGTH(@TableName, @ColumnName)";
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@TableName", "dbo." + tableName);
                cmd.Parameters.AddWithValue("@ColumnName", columnName);
                conn.Open();
                return cmd.ExecuteScalar() != DBNull.Value;
            }
        }
    }
}
