using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IImportTemplateService
    {
        IEnumerable<ExcelImportTemplate> GetList(string tableName = null, string databaseName = null);

        ExcelImportTemplate GetById(int id, string tableName = null, string databaseName = null);

        int CreateTemplate(ExcelImportTemplate template, string tableName = null, string databaseName = null);

        bool UpdateTemplate(ExcelImportTemplate template, string tableName = null, string databaseName = null);

        Task<IReadOnlyList<Dictionary<string, object>>> PreviewAsync(
            int templateId,
            Stream stream,
            string fileName,
            CancellationToken ct = default);
    }
}
