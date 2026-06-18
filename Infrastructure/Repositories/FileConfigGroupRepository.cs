using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Application.DTOs;
using Learun.DataBase.Repository;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DT_DataAcquisitionSystem.Infrastructure.Repositories
{
    public class FileConfigGroupRepository : RepositoryFactory, IFileConfigGroupRepository
    {
        private const string DefaultDb = "BaseDb";
        private const string DefaultGroupTable = "DA_AcquisitionGroup";
        private const string DefaultGroupLinkTable = "DA_AcquisitionGroup_Config";

        private bool HasColumn(string tableName, string columnName, string databaseName = DefaultDb)
        {
            string sql = "SELECT COL_LENGTH(@TableName, @ColumnName)";
            object result = this.BaseRepository(databaseName).FindObject(sql, new { TableName = "dbo." + tableName, ColumnName = columnName });
            return result != null && result != DBNull.Value;
        }

        #region Group Query

        public IEnumerable<AcquisitionGroupDto> GetList(string tableName = DefaultGroupTable, string linkTableName = DefaultGroupLinkTable, string databaseName = DefaultDb)
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            linkTableName = string.IsNullOrEmpty(linkTableName) ? DefaultGroupLinkTable : linkTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            bool hasExportProcedureColumn = HasColumn(tableName, "ExportProcedureName", databaseName);
            string exportProcedureSelect = hasExportProcedureColumn
                ? ", g.ExportProcedureName"
                : ", CAST('' AS NVARCHAR(200)) AS ExportProcedureName";

            string groupSql = $@"
                SELECT 
                    g.Id, g.GroupName, g.GroupCategory, g.GroupType, g.IsEnabled{exportProcedureSelect},
                    (SELECT COUNT(1) FROM [{linkTableName}] c WHERE c.GroupId = g.Id) AS ConfigCount
                FROM [{tableName}] g
                ORDER BY g.Id ASC";

            var groups = this.BaseRepository(databaseName).FindList<AcquisitionGroupDto>(groupSql).ToList();

            if (groups.Count == 0) return groups;

            var groupIds = groups.Select(x => $"'{x.Id}'");
            string idInClause = string.Join(",", groupIds);

            string configSql = $@"
                SELECT 
                    gc.GroupId,
                    c.Id,
                    c.EqName
                FROM [{linkTableName}] gc
                INNER JOIN DA_AcquisitionConfig c ON gc.ConfigId = c.Id
                WHERE gc.GroupId IN ({idInClause})";

            var configFlatList = this.BaseRepository(databaseName).FindList<GroupConfigFlatDto>(configSql);

            var configLookup = configFlatList
                .Where(x => !string.IsNullOrEmpty(x.EqName))
                .ToLookup(x => x.GroupId);

            foreach (var group in groups)
            {
                group.AssociatedConfigs = configLookup[group.Id]
                    .Select(x => new AcquisitionConfigDto { Id = x.Id, EqName = x.EqName })
                    .ToList();
            }

            return groups;
        }

        public IEnumerable<AcquisitionGroup> GetListByIds(IEnumerable<string> ids, string tableName = DefaultGroupTable, string databaseName = DefaultDb)
        {
            if (ids == null || !ids.Any())
                return Enumerable.Empty<AcquisitionGroup>();

            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            bool hasExportProcedureColumn = HasColumn(tableName, "ExportProcedureName", databaseName);
            string exportProcedureSelect = hasExportProcedureColumn
                ? ", [ExportProcedureName]"
                : ", CAST('' AS NVARCHAR(200)) AS ExportProcedureName";

            string sql = $"SELECT [Id], [GroupName], [GroupCategory], [GroupType], [IsEnabled]{exportProcedureSelect} FROM [{tableName}] WHERE [Id] IN @Ids ORDER BY [Id] ASC";
            return this.BaseRepository(databaseName).FindList<AcquisitionGroup>(sql, new { Ids = ids });
        }

        public IEnumerable<AcquisitionConfigStatus> GetStatusListByIds(IEnumerable<string> ids, string columnName = "GroupName", string tableName = DefaultGroupTable, string databaseName = DefaultDb)
        {
            if (ids == null || !ids.Any())
                return Enumerable.Empty<AcquisitionConfigStatus>();

            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;
            columnName = string.IsNullOrEmpty(columnName) ? "GroupName" : columnName;

            string sql = $@"
                SELECT [Id], 
                       [{columnName}] AS [Name], 
                       [IsEnabled] 
                FROM [{tableName}] 
                WHERE [Id] IN @Ids";

            return this.BaseRepository(databaseName).FindList<AcquisitionConfigStatus>(sql, new { Ids = ids });
        }

        #endregion

        #region Group CUD

        public int Insert<T>(T entity, string tableName = DefaultGroupTable, string databaseName = DefaultDb) where T : class
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            bool hasExportProcedureColumn = HasColumn(tableName, "ExportProcedureName", databaseName);
            string sql = hasExportProcedureColumn
                ? $@"
                INSERT INTO [{tableName}] (
                    [GroupName], [GroupCategory], [GroupType], [ExportProcedureName], [IsEnabled]
                ) 
                VALUES (
                    @GroupName, @GroupCategory, @GroupType, @ExportProcedureName, @IsEnabled
                );
                SELECT SCOPE_IDENTITY();"
                : $@"
                INSERT INTO [{tableName}] (
                    [GroupName], [GroupCategory], [GroupType], [IsEnabled]
                ) 
                VALUES (
                    @GroupName, @GroupCategory, @GroupType, @IsEnabled
                );
                SELECT SCOPE_IDENTITY();";

            object result = this.BaseRepository(databaseName).FindObject(sql, entity);
            return Convert.ToInt32(result);
        }

        public bool Update<T>(T entity, string tableName = DefaultGroupTable, string databaseName = DefaultDb) where T : class
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            bool hasExportProcedureColumn = HasColumn(tableName, "ExportProcedureName", databaseName);
            string exportProcedureUpdate = hasExportProcedureColumn ? "ExportProcedureName = @ExportProcedureName," : string.Empty;
            string sql = $@"
                UPDATE [{tableName}] 
                SET 
                    GroupName = @GroupName, 
                    GroupCategory = @GroupCategory, 
                    GroupType = @GroupType, 
                    {exportProcedureUpdate}
                    IsEnabled = @IsEnabled
                WHERE Id = @Id";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, entity) > 0;
        }

        public bool Delete<T>(IEnumerable<T> keyValue, string tableName = DefaultGroupTable, string databaseName = DefaultDb) where T : class
        {
            if (keyValue == null || !keyValue.Any()) return false;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"DELETE FROM [{tableName}] WHERE Id IN @Ids";
            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { Ids = keyValue }) > 0;
        }

        public bool SetEnabled<T>(IEnumerable<T> keyValue, bool isEnabled, string tableName = DefaultGroupTable, string databaseName = DefaultDb) where T : class
        {
            if (keyValue == null || !keyValue.Any()) return false;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"UPDATE [{tableName}] SET IsEnabled = @IsEnabled WHERE Id IN @Ids";
            return this.BaseRepository(databaseName).ExecuteBySql(sql, new
            {
                IsEnabled = isEnabled ? 1 : 0,
                Ids = keyValue
            }) > 0;
        }

        #endregion

        #region 关联操作 (配置 - 配置组)

        public bool AddConfigsToGroup(int groupId, int[] configIds, string tableName = DefaultGroupTable, string groupLinkTable = DefaultGroupLinkTable, string databaseName = DefaultDb)
        {
            if (configIds == null || !configIds.Any()) return true;

            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            var parameters = configIds.Select(id => new { GroupId = groupId, ConfigId = id }).ToList();

            string sql = $@"
                IF NOT EXISTS (SELECT 1 FROM [{groupLinkTable}] WHERE GroupId = @GroupId AND ConfigId = @ConfigId)
                BEGIN
                    INSERT INTO [{groupLinkTable}] (GroupId, ConfigId) VALUES (@GroupId, @ConfigId)
                END";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, parameters) > 0;
        }

        public bool RemoveConfigsFromGroup(int groupId, int[] configIds, string groupLinkTable = DefaultGroupLinkTable, string databaseName = DefaultDb)
        {
            if (configIds == null || !configIds.Any()) return true;

            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"DELETE FROM [{groupLinkTable}] WHERE GroupId = @GroupId AND ConfigId IN @ConfigIds";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { GroupId = groupId, ConfigIds = configIds }) > 0;
        }

        public bool RemoveAllConfigsFromGroup(int groupId, string groupLinkTable = DefaultGroupLinkTable, string databaseName = DefaultDb)
        {
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"DELETE FROM [{groupLinkTable}] WHERE GroupId = @GroupId";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { GroupId = groupId }) >= 0;
        }

        #endregion
    }
}
