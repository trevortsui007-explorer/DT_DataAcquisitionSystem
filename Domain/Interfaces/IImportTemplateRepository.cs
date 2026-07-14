using System.Collections.Generic;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface IImportTemplateRepository : IRepository
    {
        IEnumerable<ExcelImportTemplate> GetList(string tableName = null, string databaseName = null);

        ExcelImportTemplate GetById(int id, string tableName = null, string databaseName = null);

        int Insert(ExcelImportTemplate template, string tableName = null, string databaseName = null);

        bool Update(ExcelImportTemplate template, string tableName = null, string databaseName = null);
    }
}
