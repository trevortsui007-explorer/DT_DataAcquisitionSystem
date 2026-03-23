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

            // 假设数据库 Id 是 int，这里尝试转换，如果 id 本身就是 int 的字符串表示
            // 如果实体 Id 是 int 类型，Dapper 会自动处理参数转换
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

        #endregion

        #region CUD

        public int Insert<T>(T entity, string tableName = DefaultTable, string databaseName = DefaultDb) where T : class
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            // 包含任务所需的字段：TaskName, TaskMode, CronExpression, IsEnabled, Description, CreateTime, UpdateTime
            string sql = $@"
                INSERT INTO [{tableName}] (
                    [TaskName], [TaskMode], [CronExpression], [IsEnabled], [Description], [CreateTime], [UpdateTime]
                ) 
                VALUES (
                    @TaskName, @TaskMode, @CronExpression, @IsEnabled, @Description, @CreateTime, @UpdateTime
                );
                SELECT SCOPE_IDENTITY();";

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

        public bool Delete<T>(IEnumerable<T> keyValue, string tableName = DefaultTable, string databaseName = DefaultDb) where T : class
        {
            if (keyValue == null || !keyValue.Any()) return false;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            // 如果需要级联删除关联表 DA_AcquisitionTask_Group，建议在 Service 层处理或此处先执行删除关联逻辑
            // 这里保持与你提供样式一致，执行主表批量删除
            string sql = $"DELETE FROM [{tableName}] WHERE [Id] IN @Ids";
            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { Ids = keyValue }) > 0;
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

            // 构造参数列表
            var parameters = groupIds.Select(gid => new { TaskId = taskId, GroupId = gid }).ToList();

            string sql = $@"
                IF NOT EXISTS (SELECT 1 FROM [{linkTable}] WHERE TaskId = @TaskId AND GroupId = @GroupId)
                BEGIN
                    INSERT INTO [{linkTable}] (TaskId, GroupId) VALUES (@TaskId, @GroupId)
                END";

            // ExecuteBySql 支持 IEnumerable 参数进行批量执行
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
        /// 批量移除多个任务的关联 (用于 Delete 时的级联)
        /// </summary>
        private bool RemoveAllGroupsForIds(IEnumerable<int> taskIds, string linkTable = DefaultLinkTable, string databaseName = DefaultDb)
        {
            if (!taskIds.Any()) return true;
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

            // 返回 int 列表
            return this.BaseRepository(databaseName).FindList<TaskGroupLink>(sql, new { TaskId = taskId }).Select(x => x.GroupId).ToList();
        }

        #endregion

        #region 简洁DTO
        /// <summary>
        /// 内部使用的轻量级 DTO，用于处理关联表查询
        /// </summary>
        public class TaskGroupLink
        {
            public int TaskId { get; set; }
            public int GroupId { get; set; }
        }
        #endregion
    }
}