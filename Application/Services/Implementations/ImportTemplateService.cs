using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Infrastructure;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class ImportTemplateService : IImportTemplateService
    {
        private const string DefaultDb = "BaseDb";
        private const string DefaultTable = "DA_ExcelImportTemplate";

        private readonly IImportTemplateRepository _repository;

        public ImportTemplateService(IImportTemplateRepository repository)
        {
            _repository = repository;
        }

        public IEnumerable<ExcelImportTemplate> GetList(string tableName = null, string databaseName = null)
        {
            return _repository.GetList(
                string.IsNullOrEmpty(tableName) ? DefaultTable : tableName,
                string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName);
        }

        public ExcelImportTemplate GetById(int id, string tableName = null, string databaseName = null)
        {
            return _repository.GetById(
                id,
                string.IsNullOrEmpty(tableName) ? DefaultTable : tableName,
                string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName);
        }

        public int CreateTemplate(ExcelImportTemplate template, string tableName = null, string databaseName = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            return _repository.Insert(
                template,
                string.IsNullOrEmpty(tableName) ? DefaultTable : tableName,
                string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName);
        }

        public bool UpdateTemplate(ExcelImportTemplate template, string tableName = null, string databaseName = null)
        {
            if (template == null || template.Id <= 0) return false;

            return _repository.Update(
                template,
                string.IsNullOrEmpty(tableName) ? DefaultTable : tableName,
                string.IsNullOrEmpty(databaseName) ? DefaultDb : databaseName);
        }

        public async Task<IReadOnlyList<Dictionary<string, object>>> PreviewAsync(
            int templateId,
            Stream stream,
            string fileName,
            CancellationToken ct = default)
        {
            var template = GetById(templateId);
            if (template == null)
            {
                throw new InvalidOperationException($"Import template not found: {templateId}");
            }

            var parser = new TemplateExcelParser();
            var config = new AcquisitionConfig
            {
                Id = 0,
                EqName = template.TemplateName,
                TableName = string.Empty,
                FileNamePattern = fileName,
                FileType = Path.GetExtension(fileName),
                StartRow = template.ParseDefinition().DataRegion?.StartRow ?? 1,
                ParserType = template.ParserType,
                TemplateId = template.Id
            };

            return await parser.ParseAsync(stream, template, config, fileName, fileName, config.StartRow, ct)
                .ConfigureAwait(false);
        }
    }
}
