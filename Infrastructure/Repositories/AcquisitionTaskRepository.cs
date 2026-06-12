using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using Learun.DataBase.Repository;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DT_DataAcquisitionSystem.Infrastructure.Repositories
{
    public class AcquisitionTaskRepository : RepositoryFactory, IAcquisitionTaskRepository
    {
        private const string DefaultDb = "BaseDb";
        private const string DefaultTable = "DA_AcquisitionTask";
        private const string DefaultLinkTable = "DA_AcquisitionTask_Group";

        private const string DefaultGroupTable = "DA_AcquisitionGroup";
        private const string DefaultGroupConfigLinkTable = "DA_AcquisitionGroup_Config";

        #region Query

        public IEnumerable<AcquisitionTask> GetList(string tableName = DefaultTable, string databaseName = DefaultDb)
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"SELECT * FROM [{tableName}] ORDER BY [CreateTime] DESC";
            return this.BaseRepository(databaseName).FindList<AcquisitionTask>(sql);
        }

        public AcquisitionTask GetById(string id, string tableName = DefaultTable, string databaseName = DefaultDb)
        {
            if (string.IsNullOrEmpty(id)) return null;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"SELECT * FROM [{tableName}] WHERE [Id] = @Id";
            return this.BaseRepository(databaseName).FindEntity<AcquisitionTask>(sql, new { Id = id });
        }

        public IEnumerable<AcquisitionTask> GetListByIds(IEnumerable<string> ids, string tableName = DefaultTable, string databaseName = DefaultDb)
        {
            if (ids == null || !ids.Any()) return Enumerable.Empty<AcquisitionTask>();

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"SELECT * FROM [{tableName}] WHERE [Id] IN @Ids ORDER BY [CreateTime] DESC";
            return this.BaseRepository(databaseName).FindList<AcquisitionTask>(sql, new { Ids = ids });
        }

        public IEnumerable<AcquisitionTask> GetListByMode(int taskMode, string tableName = DefaultTable, string databaseName = DefaultDb)
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"SELECT * FROM [{tableName}] WHERE [TaskMode] = @TaskMode ORDER BY [CreateTime] DESC";
            return this.BaseRepository(databaseName).FindList<AcquisitionTask>(sql, new { TaskMode = taskMode });
        }

        /// <summary>
        /// 获取任务列表，并附带任务关联的配置组信息。
        /// 用于 GET /api/data-acquisition/tasks。
        /// </summary>
        public IEnumerable<AcquisitionTaskDto> GetListWithGroups(
            string tableName = DefaultTable,
            string linkTable = DefaultLinkTable,
            string groupTable = DefaultGroupTable,
            string groupConfigLinkTable = DefaultGroupConfigLinkTable,
            string databaseName = DefaultDb)
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            linkTable = string.IsNullOrEmpty(linkTable) ? DefaultLinkTable : linkTable;
            groupTable = string.IsNullOrEmpty(groupTable) ? DefaultGroupTable : groupTable;
            groupConfigLinkTable = string.IsNullOrEmpty(groupConfigLinkTable) ? DefaultGroupConfigLinkTable : groupConfigLinkTable;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string taskSql = $@"
                SELECT
                    [Id],
                    [TaskName],
                    [TaskMode],
                    [CronExpression],
                    [IsEnabled],
                    [Description],
                    [CreateTime],
                    [UpdateTime]
                FROM [{tableName}]
                ORDER BY [CreateTime] DESC";

            var tasks = this.BaseRepository(databaseName)
                .FindList<AcquisitionTaskDto>(taskSql)
                .ToList();

            if (!tasks.Any())
            {
                return tasks;
            }

            var taskIds = tasks.Select(x => x.Id).ToList();

            var groupRecords = GetAssociatedGroupsByTaskIds(
                taskIds,
                linkTable,
                groupTable,
                groupConfigLinkTable,
                databaseName
            ).ToList();

            AttachGroups(tasks, groupRecords);

            return tasks;
        }

        /// <summary>
        /// 获取单个任务详情，并附带任务关联的配置组信息。
        /// 用于 GET /api/data-acquisition/tasks/{id}。
        /// </summary>
        public AcquisitionTaskDto GetByIdWithGroups(
            string id,
            string tableName = DefaultTable,
            string linkTable = DefaultLinkTable,
            string groupTable = DefaultGroupTable,
            string groupConfigLinkTable = DefaultGroupConfigLinkTable,
            string databaseName = DefaultDb)
        {
            if (string.IsNullOrEmpty(id)) return null;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            linkTable = string.IsNullOrEmpty(linkTable) ? DefaultLinkTable : linkTable;
            groupTable = string.IsNullOrEmpty(groupTable) ? DefaultGroupTable : groupTable;
            groupConfigLinkTable = string.IsNullOrEmpty(groupConfigLinkTable) ? DefaultGroupConfigLinkTable : groupConfigLinkTable;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string taskSql = $@"
                SELECT
                    [Id],
                    [TaskName],
                    [TaskMode],
                    [CronExpression],
                    [IsEnabled],
                    [Description],
                    [CreateTime],
                    [UpdateTime]
                FROM [{tableName}]
                WHERE [Id] = @Id";

            var task = this.BaseRepository(databaseName)
                .FindEntity<AcquisitionTaskDto>(taskSql, new { Id = id });

            if (task == null)
            {
                return null;
            }

            var groupRecords = GetAssociatedGroupsByTaskIds(
                new List<int> { task.Id },
                linkTable,
                groupTable,
                groupConfigLinkTable,
                databaseName
            ).ToList();

            AttachGroups(new List<AcquisitionTaskDto> { task }, groupRecords);

            return task;
        }

        #endregion

        #region CUD

        public int Insert<T>(T entity, string tableName = DefaultTable, string databaseName = DefaultDb) where T : class
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $@"
                INSERT INTO [{tableName}] (
                    [TaskName], [TaskMode], [CronExpression], [IsEnabled], [Description], [CreateTime], [UpdateTime]
                )
                OUTPUT INSERTED.[Id]
                VALUES (
                    @TaskName, @TaskMode, @CronExpression, @IsEnabled, @Description, @CreateTime, @UpdateTime
                );";

            object result = this.BaseRepository(databaseName).FindObject(sql, entity);
            return result != null ? Convert.ToInt32(result) : 0;
        }

        public bool Update<T>(T entity, string tableName = DefaultTable, string databaseName = DefaultDb) where T : class
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $@"
                UPDATE [{tableName}] 
                SET 
                    [TaskName] = @TaskName, 
                    [TaskMode] = @TaskMode, 
                    [CronExpression] = @CronExpression, 
                    [IsEnabled] = @IsEnabled,
                    [Description] = @Description,
                    [UpdateTime] = @UpdateTime
                WHERE [Id] = @Id";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, entity) > 0;
        }

        /// <summary>
        /// 删除任务。
        /// 先删除 DA_AcquisitionTask_Group 关联关系，再删除任务主表，避免残留脏关系。
        /// </summary>
        public bool Delete<T>(IEnumerable<T> keyValue, string tableName = DefaultTable, string databaseName = DefaultDb) where T : class
        {
            if (keyValue == null || !keyValue.Any()) return false;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            var ids = keyValue.Select(x => Convert.ToInt32(x)).ToList();

            RemoveAllGroupsForIds(ids, DefaultLinkTable, databaseName);

            string sql = $"DELETE FROM [{tableName}] WHERE [Id] IN @Ids";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { Ids = ids }) > 0;
        }

        public bool SetEnabled<T>(IEnumerable<T> keyValue, bool isEnabled, string tableName = DefaultTable, string databaseName = DefaultDb) where T : class
        {
            if (keyValue == null || !keyValue.Any()) return false;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"UPDATE [{tableName}] SET [IsEnabled] = @IsEnabled, [UpdateTime] = @UpdateTime WHERE [Id] IN @Ids";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, new
            {
                IsEnabled = isEnabled ? 1 : 0,
                UpdateTime = DateTime.Now,
                Ids = keyValue
            }) > 0;
        }

        #endregion

        #region 关联操作 (任务 - 配置组)

        /// <summary>
        /// 将任务关联到多个组
        /// </summary>
        public bool AddToGroups(int taskId, IEnumerable<int> groupIds, string linkTable = DefaultLinkTable, string databaseName = DefaultDb)
        {
            if (groupIds == null || !groupIds.Any()) return true;

            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            var parameters = groupIds.Select(gid => new { TaskId = taskId, GroupId = gid }).ToList();

            string sql = $@"
                IF NOT EXISTS (SELECT 1 FROM [{linkTable}] WHERE TaskId = @TaskId AND GroupId = @GroupId)
                BEGIN
                    INSERT INTO [{linkTable}] (TaskId, GroupId) VALUES (@TaskId, @GroupId)
                END";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, parameters) > 0;
        }

        /// <summary>
        /// 从多个组中移除任务
        /// </summary>
        public bool RemoveFromGroups(int taskId, IEnumerable<int> groupIds, string linkTable = DefaultLinkTable, string databaseName = DefaultDb)
        {
            if (groupIds == null || !groupIds.Any()) return true;

            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"DELETE FROM [{linkTable}] WHERE TaskId = @TaskId AND GroupId IN @GroupIds";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { TaskId = taskId, GroupIds = groupIds }) > 0;
        }

        /// <summary>
        /// 移除任务的所有组关联
        /// </summary>
        public bool RemoveAllGroups(int taskId, string linkTable = DefaultLinkTable, string databaseName = DefaultDb)
        {
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"DELETE FROM [{linkTable}] WHERE TaskId = @TaskId";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { TaskId = taskId }) >= 0;
        }

        /// <summary>
        /// 批量移除多个任务的关联，用于 Delete 时级联清理
        /// </summary>
        private bool RemoveAllGroupsForIds(IEnumerable<int> taskIds, string linkTable = DefaultLinkTable, string databaseName = DefaultDb)
        {
            if (taskIds == null || !taskIds.Any()) return true;

            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"DELETE FROM [{linkTable}] WHERE TaskId IN @TaskIds";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { TaskIds = taskIds }) >= 0;
        }

        /// <summary>
        /// 获取任务所属的组 ID 列表
        /// </summary>
        public IEnumerable<int> GetGroupIdsByTaskId(int taskId, string linkTable = DefaultLinkTable, string databaseName = DefaultDb)
        {
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"SELECT TaskId, GroupId FROM [{linkTable}] WHERE TaskId = @TaskId";

            return this.BaseRepository(databaseName)
                .FindList<TaskGroupLink>(sql, new { TaskId = taskId })
                .Select(x => x.GroupId)
                .ToList();
        }

        #endregion

        #region DTO 组装

        /// <summary>
        /// 根据任务 ID 列表查询任务关联的配置组信息。
        /// </summary>
        private IEnumerable<TaskAssociatedGroupRecord> GetAssociatedGroupsByTaskIds(
            IEnumerable<int> taskIds,
            string linkTable = DefaultLinkTable,
            string groupTable = DefaultGroupTable,
            string groupConfigLinkTable = DefaultGroupConfigLinkTable,
            string databaseName = DefaultDb)
        {
            if (taskIds == null || !taskIds.Any())
            {
                return Enumerable.Empty<TaskAssociatedGroupRecord>();
            }

            linkTable = string.IsNullOrEmpty(linkTable) ? DefaultLinkTable : linkTable;
            groupTable = string.IsNullOrEmpty(groupTable) ? DefaultGroupTable : groupTable;
            groupConfigLinkTable = string.IsNullOrEmpty(groupConfigLinkTable) ? DefaultGroupConfigLinkTable : groupConfigLinkTable;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $@"
                SELECT
                    tg.[TaskId],
                    g.[Id],
                    g.[GroupName],
                    g.[GroupCategory],
                    g.[GroupType],
                    g.[IsEnabled],
                    COUNT(gc.[ConfigId]) AS [ConfigCount]
                FROM [{linkTable}] tg
                INNER JOIN [{groupTable}] g
                    ON tg.[GroupId] = g.[Id]
                LEFT JOIN [{groupConfigLinkTable}] gc
                    ON gc.[GroupId] = g.[Id]
                WHERE tg.[TaskId] IN @TaskIds
                GROUP BY
                    tg.[TaskId],
                    g.[Id],
                    g.[GroupName],
                    g.[GroupCategory],
                    g.[GroupType],
                    g.[IsEnabled]
                ORDER BY
                    tg.[TaskId],
                    g.[Id]";

            return this.BaseRepository(databaseName)
                .FindList<TaskAssociatedGroupRecord>(sql, new { TaskIds = taskIds });
        }

        /// <summary>
        /// 把关联组 records 组装到任务 DTO 上。
        /// </summary>
        private void AttachGroups(
            List<AcquisitionTaskDto> tasks,
            List<TaskAssociatedGroupRecord> groupRecords)
        {
            var groupMap = groupRecords
                .GroupBy(x => x.TaskId)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(g => new TaskAssociatedGroupDto
                    {
                        Id = g.Id,
                        GroupName = g.GroupName,
                        GroupCategory = g.GroupCategory,
                        GroupType = g.GroupType,
                        ConfigCount = g.ConfigCount,
                        IsEnabled = g.IsEnabled
                    }).ToList()
                );

            foreach (var task in tasks)
            {
                if (groupMap.TryGetValue(task.Id, out var groups))
                {
                    task.AssociatedGroups = groups;
                    task.GroupCount = groups.Count;
                    task.GroupIds = groups.Select(x => x.Id).ToList();
                }
                else
                {
                    task.AssociatedGroups = new List<TaskAssociatedGroupDto>();
                    task.GroupCount = 0;
                    task.GroupIds = new List<int>();
                }
            }
        }

        #endregion

        #region 简洁DTO

        /// <summary>
        /// 内部使用的轻量级 DTO，用于处理任务-配置组关联表查询
        /// </summary>
        public class TaskGroupLink
        {
            public int TaskId { get; set; }
            public int GroupId { get; set; }
        }

        /// <summary>
        /// 内部使用的轻量级 DTO，用于查询任务关联的配置组
        /// </summary>
        public class TaskAssociatedGroupRecord : TaskAssociatedGroupDto
        {
            public int TaskId { get; set; }
        }

        #endregion
    }
}
