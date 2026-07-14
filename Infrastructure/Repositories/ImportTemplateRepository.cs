using System;
using System.Collections.Generic;
using System.Linq;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using Learun.DataBase.Repository;

namespace DT_DataAcquisitionSystem.Infrastructure.Repositories
{
    public class ImportTemplateRepository : RepositoryFactory, IImportTemplateRepository
    {
        private const string DefaultDb = "BaseDb";
        private const string DefaultTable = "DA_ExcelImportTemplate";

        public IEnumerable<ExcelImportTemplate> GetList(string tableName = DefaultTable, string databaseName = DefaultDb)
        {
            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $@"
                SELECT *
                FROM [{tableName}]
                WHERE [IsEnabled] = 1
                ORDER BY [TemplateCode], [TemplateVersion] DESC";

            return this.BaseRepository(databaseName).FindList<ExcelImportTemplate>(sql);
        }

        public ExcelImportTemplate GetById(int id, string tableName = DefaultTable, string databaseName = DefaultDb)
        {
            if (id <= 0) return null;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"SELECT * FROM [{tableName}] WHERE [Id] = @Id";
            return this.BaseRepository(databaseName).FindEntity<ExcelImportTemplate>(sql, new { Id = id });
        }

        public int Insert(ExcelImportTemplate template, string tableName = DefaultTable, string databaseName = DefaultDb)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $@"
                INSERT INTO [{tableName}]
                (
                    [TemplateCode], [TemplateName], [ParserType], [TemplateVersion],
                    [DefinitionJson], [IsEnabled], [CreateTime], [UpdateTime]
                )
                VALUES
                (
                    @TemplateCode, @TemplateName, @ParserType, @TemplateVersion,
                    @DefinitionJson, @IsEnabled, GETDATE(), GETDATE()
                );
                SELECT SCOPE_IDENTITY();";

            object result = this.BaseRepository(databaseName).FindObject(sql, template);
            return Convert.ToInt32(result);
        }

        public bool Update(ExcelImportTemplate template, string tableName = DefaultTable, string databaseName = DefaultDb)
        {
            if (template == null || template.Id <= 0) return false;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $@"
                UPDATE [{tableName}]
                SET
                    [TemplateCode] = @TemplateCode,
                    [TemplateName] = @TemplateName,
                    [ParserType] = @ParserType,
                    [TemplateVersion] = @TemplateVersion,
                    [DefinitionJson] = @DefinitionJson,
                    [IsEnabled] = @IsEnabled,
                    [UpdateTime] = GETDATE()
                WHERE [Id] = @Id";

            return this.BaseRepository(databaseName).ExecuteBySql(sql, template) > 0;
        }

        public int Insert<T>(T entity, string tableName, string databaseName) where T : class
        {
            return Insert(entity as ExcelImportTemplate, tableName, databaseName);
        }

        public bool Update<T>(T entity, string tableName, string databaseName) where T : class
        {
            return Update(entity as ExcelImportTemplate, tableName, databaseName);
        }

        public bool Delete<T>(IEnumerable<T> keyValue, string tableName, string databaseName) where T : class
        {
            if (keyValue == null || !keyValue.Any()) return false;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"DELETE FROM [{tableName}] WHERE [Id] IN @Ids";
            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { Ids = keyValue }) > 0;
        }

        public bool SetEnabled<T>(IEnumerable<T> keyValue, bool isEnabled, string tableName, string databaseName) where T : class
        {
            if (keyValue == null || !keyValue.Any()) return false;

            tableName = string.IsNullOrEmpty(tableName) ? DefaultTable : tableName;
            databaseName = string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName;

            string sql = $"UPDATE [{tableName}] SET [IsEnabled] = @IsEnabled, [UpdateTime] = GETDATE() WHERE [Id] IN @Ids";
            return this.BaseRepository(databaseName).ExecuteBySql(sql, new { IsEnabled = isEnabled, Ids = keyValue }) > 0;
        }
    }
}
