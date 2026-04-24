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

        #region Group Query

        public IEnumerable<AcquisitionGroupDto> GetList(string tableName = DefaultGroupTable, string linkTableName = DefaultGroupLinkTable, string databaseName = DefaultDb)
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            linkTableName = string.IsNullOrEmpty(linkTableName) ? DefaultGroupLinkTable : linkTableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            // Step 1：查 Group
            string groupSql = $@"
                SELECT 
                    g.*, 
                    (SELECT COUNT(*) FROM [{linkTableName}] c WHERE c.GroupId = g.Id) AS ConfigCount
                FROM [{tableName}] g
                ORDER BY g.SortOrder ASC";

            var groups = this.BaseRepository(databaseName).FindList<AcquisitionGroupDto>(groupSql).ToList();

            // Step 2：查 Config（扁平）
            string configSql = $@"
                SELECT 
                    gc.GroupId,
                    c.EqName
                FROM [{linkTableName}] gc
                LEFT JOIN DA_AcquisitionConfig c ON gc.ConfigId = c.Id";

            var configFlatList = this.BaseRepository(databaseName).FindList<GroupConfigFlatDto>(configSql);

            // Step 3：分组（GroupId → List<AcquisitionConfigDto>）
            var configDict = configFlatList
                .Where(x => x.EqName != null)
                .GroupBy(x => x.GroupId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => new AcquisitionConfigDto
                    {
                        EqName = x.EqName
                    }).ToList()
                );

            // Step 4：组装到 Group
            foreach (var group in groups)
            {
                group.AssociatedConfigs = configDict.ContainsKey(group.Id)
                    ? configDict[group.Id]
                    : new List<AcquisitionConfigDto>();
            }

            return groups;
        }

        public IEnumerable<AcquisitionGroup> GetListByIds(IEnumerable<string> ids, string tableName = DefaultGroupTable, string databaseName = DefaultDb)
        {
            if (ids == null || !ids.Any())
                return Enumerable.Empty<AcquisitionGroup>();

            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"SELECT * FROM [{tableName}] WHERE [Id] IN @Ids ORDER BY [SortOrder] ASC";
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

            // 对应你提供的：GroupName, GroupCategory, GroupType, SortOrder, IsEnabled
            string sql = $@"
                INSERT INTO [{tableName}] (
                    [GroupName], [GroupCategory], [GroupType], [SortOrder], [IsEnabled]
                ) 
                VALUES (
                    @GroupName, @GroupCategory, @GroupType, @SortOrder, @IsEnabled
                );
                SELECT SCOPE_IDENTITY();";

            object result = this.BaseRepository(databaseName).FindObject(sql, entity);
            return Convert.ToInt32(result);
        }

        public bool Update<T>(T entity, string tableName = DefaultGroupTable, string databaseName = DefaultDb) where T : class
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $@"
                UPDATE [{tableName}] 
                SET 
                    GroupName = @GroupName, 
                    GroupCategory = @GroupCategory, 
                    GroupType = @GroupType, 
                    SortOrder = @SortOrder,
                    IsEnabled = @IsEnabled
                WHERE Id = @Id";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, entity) > 0;
        }

        public bool Delete<T>(IEnumerable<T> keyValue, string tableName = DefaultGroupTable, string databaseName = DefaultDb) where T : class
        {
            if (keyValue == null || !keyValue.Any()) return false;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultGroupTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            // 注意：删除组时，可能需要考虑是否同步删除 DA_AcquisitionGroup_Config 中的关联数据
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

        public bool RemoveAllConfigsFromGroup(int groupId,string groupLinkTable = DefaultGroupLinkTable, string databaseName = DefaultDb)
        {
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"DELETE FROM [{groupLinkTable}] WHERE GroupId = @GroupId";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { GroupId = groupId }) >= 0;
        }

        #endregion
    }
}