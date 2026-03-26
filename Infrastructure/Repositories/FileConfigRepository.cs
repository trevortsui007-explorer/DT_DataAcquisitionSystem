using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using Learun.DataBase.Repository;

namespace DT_DataAcquisitionSystem.Infrastructure.Repositories
{
    public class FileConfigRepository : RepositoryFactory, IFileConfigRepository
    {
        private const string DefaultDb = "BaseDb";
        private const string DefaultConfigTable = "DA_AcquisitionConfig";
        private const string DefaultGroupLinkTable = "DA_AcquisitionGroup_Config";

        #region Config Query
        public (int Total, IEnumerable<AcquisitionConfig> List) GetPageList(
            FileConfigQueryOptions options,
            int page,
            int limit,
            string tableName = DefaultConfigTable,
            string databaseName = DefaultDb)
        {
            var db = databaseName ?? DefaultDb;
            var table = tableName ?? DefaultConfigTable;

            // 1. 构建基础 WHERE 条件
            string whereSql = " WHERE 1=1 ";
            var parameters = new DynamicParameters();

            if (options.Ids != null && options.Ids.Any())
            {
                whereSql += " AND [Id] IN @Ids ";
                parameters.Add("Ids", options.Ids);
            }

            // 2. 查询总数
            string countSql = $"SELECT COUNT(1) FROM [{table}] {whereSql}";
            int total = (int)this.BaseRepository(db).FindObject(countSql, parameters);

            // 3. 查询分页数据
            string dataSql = $@"
                SELECT * FROM [{table}] 
                {whereSql} 
                ORDER BY [Id] DESC 
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

            parameters.Add("Skip", (page - 1) * limit);
            parameters.Add("Take", limit);

            var list = this.BaseRepository(db).FindList<AcquisitionConfig>(dataSql, parameters);

            return (total, list);
        }

        public IEnumerable<AcquisitionConfig> GetList(string tableName = DefaultConfigTable, string databaseName = DefaultDb)
        {
            var db = databaseName ?? DefaultDb;
            var table = tableName ?? DefaultConfigTable;

            string sql = $"SELECT * FROM [{table}]";
            return this.BaseRepository(db).FindList<AcquisitionConfig>(sql);
        }

        public IEnumerable<AcquisitionConfig> GetListByIds(IEnumerable<string> ids,
            string tableName = DefaultConfigTable,
            string databaseName = DefaultDb)
        {
            if (ids == null || !ids.Any())
                return Enumerable.Empty<AcquisitionConfig>();

            var db = databaseName ?? DefaultDb;
            var table = tableName ?? DefaultConfigTable;

            string sql = $"SELECT * FROM [{table}] WHERE [Id] IN @Ids";
            return this.BaseRepository(db)
                .FindList<AcquisitionConfig>(sql, new { Ids = ids });
        }

        public IEnumerable<AcquisitionConfig> GetListByGroupIds(IEnumerable<string> groupIds,
            string tableName = DefaultConfigTable,
            string linkTableName = DefaultGroupLinkTable,
            string databaseName = DefaultDb)
        {
            if (groupIds == null || !groupIds.Any())
                return Enumerable.Empty<AcquisitionConfig>();

            var db = databaseName ?? DefaultDb;
            var table = tableName ?? DefaultConfigTable;
            var linkTable = linkTableName ?? DefaultGroupLinkTable;

            string sql = $@"
            SELECT DISTINCT c.*
            FROM [{table}] c
            INNER JOIN [{linkTable}] gc ON c.Id = gc.ConfigId
            WHERE gc.GroupId IN @GroupIds
              AND gc.IsEnabled = 1
              AND c.IsEnabled = 1";

            return this.BaseRepository(db).FindList<AcquisitionConfig>(sql, new { GroupIds = groupIds });
        }

        public IEnumerable<AcquisitionConfig> GetListByTaskIds(IEnumerable<string> taskIds,
            string tableName = DefaultConfigTable,
            string databaseName = DefaultDb)
        {
            if (taskIds == null || !taskIds.Any())
                return Enumerable.Empty<AcquisitionConfig>();

            var db = databaseName ?? DefaultDb;
            var table = tableName ?? DefaultConfigTable;

            string sql = $@"
                SELECT DISTINCT c.*
                FROM [{table}] c
                INNER JOIN [DA_AcquisitionGroup_Config] gc ON c.Id = gc.ConfigId
                INNER JOIN [DA_AcquisitionTask_Group] tg ON gc.GroupId = tg.GroupId
                INNER JOIN [DA_AcquisitionTask] t ON tg.TaskId = t.Id
                WHERE t.Id IN @TaskIds
                  AND c.IsEnabled = 1
                  AND gc.IsEnabled = 1
                  AND t.IsEnabled = 1";

            return this.BaseRepository(db)
                .FindList<AcquisitionConfig>(sql, new { TaskIds = taskIds });
        }

        public IEnumerable<AcquisitionConfigStatus> GetStatusListByIds(IEnumerable<string> ids,
            string columnName = "EqName",
            string tableName = DefaultConfigTable,
            string databaseName = DefaultDb)
        {
            if (ids == null || !ids.Any())
                return Enumerable.Empty<AcquisitionConfigStatus>();

            var col = columnName ?? "EqName";
            var db = databaseName ?? DefaultDb;
            var table = tableName ?? DefaultConfigTable;

            string sql = $@"
                SELECT [Id],
                       [{col}] AS [Name],
                       [IsEnabled]
                FROM [{table}]
                WHERE [Id] IN @Ids";

            return this.BaseRepository(db)
                .FindList<AcquisitionConfigStatus>(sql, new { Ids = ids });
        }

        #endregion


        #region Config CUD

        public int Insert<T>(T config,
            string tableName = DefaultConfigTable,
            string databaseName = DefaultDb) where T : class
        {
            string sql = $@"
                INSERT INTO [{tableName}](
                    [EqName],[TableName],[FilePathPattern],[FileNamePattern],[FileType],
                    [HeaderRow],[StartRow],[FieldMappings],[IsEnabled],
                    [PostProcessingType],[ProcedureName],[ServiceName],[Flag],[FlagName]
                )
                VALUES(
                    @EqName,@TableName,@FilePathPattern,@FileNamePattern,@FileType,
                    @HeaderRow,@StartRow,@FieldMappings,@IsEnabled,
                    @PostProcessingType,@ProcedureName,@ServiceName,@Flag,@FlagName
                );
                SELECT SCOPE_IDENTITY();";

            object result = this.BaseRepository(databaseName)
                .FindObject(sql, config);

            return Convert.ToInt32(result);
        }

        public bool Update<T>(T config,
            string tableName = DefaultConfigTable,
            string databaseName = DefaultDb) where T : class
        {
            string sql = $@"
                UPDATE [{tableName}]
                SET
                    EqName = @EqName,
                    TableName = @TableName,
                    FilePathPattern = @FilePathPattern,
                    FileNamePattern = @FileNamePattern,
                    FileType = @FileType,
                    HeaderRow = @HeaderRow,
                    StartRow = @StartRow,
                    FieldMappings = @FieldMappings,
                    IsEnabled = @IsEnabled,
                    PostProcessingType = @PostProcessingType,
                    ProcedureName = @ProcedureName,
                    ServiceName = @ServiceName,
                    Flag = @Flag,
                    FlagName = @FlagName
                WHERE Id = @Id";

            return this.BaseRepository(databaseName)
                .ExecuteBySql(sql, config) > 0;
        }

        public bool Delete<T>(IEnumerable<T> ids,
            string tableName = DefaultConfigTable,
            string databaseName = DefaultDb) where T : class
        {
            string sql = $"DELETE FROM [{tableName}] WHERE Id IN @Ids";

            return this.BaseRepository(databaseName)
                .ExecuteBySql(sql, new { Ids = ids }) > 0;
        }

        public bool SetEnabled<T>(IEnumerable<T> ids,
            bool isEnabled,
            string tableName = DefaultConfigTable,
            string databaseName = DefaultDb) where T : class
        {
            string sql = $"UPDATE [{tableName}] SET IsEnabled = @IsEnabled WHERE Id IN @Ids";

            return this.BaseRepository(databaseName)
                .ExecuteBySql(sql, new
                {
                    IsEnabled = isEnabled ? 1 : 0,
                    Ids = ids
                }) > 0;
        }

        #endregion
    }
}