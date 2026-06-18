using System;
using System.Configuration;
using System.Data.SqlClient;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Infrastructure.Repositories
{
    public class ReportExportTaskRepository : IReportExportTaskRepository
    {
        private readonly string _connectionString;

        public ReportExportTaskRepository()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["BaseDb"]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                throw new InvalidOperationException("未配置 BaseDb 连接字符串。");
            }
        }

        public void EnsureStorage()
        {
            const string sql = @"
IF OBJECT_ID('dbo.DA_ReportExportTask', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DA_ReportExportTask (
        Id NVARCHAR(64) NOT NULL PRIMARY KEY,
        GroupIds NVARCHAR(1000) NOT NULL,
        StartTime DATETIME NOT NULL,
        EndTime DATETIME NOT NULL,
        Status NVARCHAR(32) NOT NULL,
        Progress INT NOT NULL,
        Stage NVARCHAR(100) NULL,
        FilePath NVARCHAR(1000) NULL,
        FileName NVARCHAR(255) NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        CreatedAt DATETIME NOT NULL,
        ExpiredAt DATETIME NOT NULL
    );
END";
            Execute(sql);
        }

        public void Create(ReportExportTask task)
        {
            const string sql = @"
INSERT INTO dbo.DA_ReportExportTask
(Id, GroupIds, StartTime, EndTime, Status, Progress, Stage, FilePath, FileName, ErrorMessage, CreatedAt, ExpiredAt)
VALUES
(@Id, @GroupIds, @StartTime, @EndTime, @Status, @Progress, @Stage, @FilePath, @FileName, @ErrorMessage, @CreatedAt, @ExpiredAt);";
            Execute(sql, cmd => AddTaskParameters(cmd, task));
        }

        public ReportExportTask GetById(string id)
        {
            const string sql = @"
SELECT Id, GroupIds, StartTime, EndTime, Status, Progress, Stage, FilePath, FileName, ErrorMessage, CreatedAt, ExpiredAt
FROM dbo.DA_ReportExportTask
WHERE Id = @Id";

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new ReportExportTask
                    {
                        Id = reader["Id"].ToString(),
                        GroupIds = reader["GroupIds"].ToString(),
                        StartTime = Convert.ToDateTime(reader["StartTime"]),
                        EndTime = Convert.ToDateTime(reader["EndTime"]),
                        Status = reader["Status"].ToString(),
                        Progress = Convert.ToInt32(reader["Progress"]),
                        Stage = reader["Stage"] as string,
                        FilePath = reader["FilePath"] as string,
                        FileName = reader["FileName"] as string,
                        ErrorMessage = reader["ErrorMessage"] as string,
                        CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                        ExpiredAt = Convert.ToDateTime(reader["ExpiredAt"])
                    };
                }
            }
        }

        public void UpdateProgress(string id, string status, int progress, string stage, string errorMessage = null)
        {
            const string sql = @"
UPDATE dbo.DA_ReportExportTask
SET Status = @Status, Progress = @Progress, Stage = @Stage, ErrorMessage = @ErrorMessage
WHERE Id = @Id";
            Execute(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@Progress", progress);
                cmd.Parameters.AddWithValue("@Stage", (object)stage ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ErrorMessage", (object)errorMessage ?? DBNull.Value);
            });
        }

        public void Complete(string id, string filePath, string fileName)
        {
            const string sql = @"
UPDATE dbo.DA_ReportExportTask
SET Status = @Status, Progress = 100, Stage = @Stage, FilePath = @FilePath, FileName = @FileName, ErrorMessage = NULL
WHERE Id = @Id";
            Execute(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Status", ReportExportTaskStatus.Success);
                cmd.Parameters.AddWithValue("@Stage", "导出完成");
                cmd.Parameters.AddWithValue("@FilePath", filePath);
                cmd.Parameters.AddWithValue("@FileName", fileName);
            });
        }

        public void Fail(string id, string errorMessage)
        {
            const string sql = @"
UPDATE dbo.DA_ReportExportTask
SET Status = @Status, Stage = @Stage, ErrorMessage = @ErrorMessage
WHERE Id = @Id";
            Execute(sql, cmd =>
            {
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@Status", ReportExportTaskStatus.Failed);
                cmd.Parameters.AddWithValue("@Stage", "导出失败");
                cmd.Parameters.AddWithValue("@ErrorMessage", (object)errorMessage ?? DBNull.Value);
            });
        }

        private void Execute(string sql, Action<SqlCommand> bind = null)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                bind?.Invoke(cmd);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static void AddTaskParameters(SqlCommand cmd, ReportExportTask task)
        {
            cmd.Parameters.AddWithValue("@Id", task.Id);
            cmd.Parameters.AddWithValue("@GroupIds", task.GroupIds);
            cmd.Parameters.AddWithValue("@StartTime", task.StartTime);
            cmd.Parameters.AddWithValue("@EndTime", task.EndTime);
            cmd.Parameters.AddWithValue("@Status", task.Status);
            cmd.Parameters.AddWithValue("@Progress", task.Progress);
            cmd.Parameters.AddWithValue("@Stage", (object)task.Stage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FilePath", (object)task.FilePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FileName", (object)task.FileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ErrorMessage", (object)task.ErrorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", task.CreatedAt);
            cmd.Parameters.AddWithValue("@ExpiredAt", task.ExpiredAt);
        }
    }
}
