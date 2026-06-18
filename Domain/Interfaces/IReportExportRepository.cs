using System;
using System.Collections.Generic;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface IReportExportTaskRepository
    {
        void EnsureStorage();
        void Create(ReportExportTask task);
        ReportExportTask GetById(string id);
        void UpdateProgress(string id, string status, int progress, string stage, string errorMessage = null);
        void Complete(string id, string filePath, string fileName);
        void Fail(string id, string errorMessage);
    }

    public class ReportExportGroupDefinition
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; }
        public string ExportProcedureName { get; set; }
    }

    public class ReportDataSet
    {
        public string SheetName { get; set; }
        public System.Data.DataTable Data { get; set; }
    }

    public interface IReportExportDataProvider
    {
        IList<ReportExportGroupDefinition> GetGroupDefinitions(IEnumerable<int> groupIds);
        IList<ReportDataSet> ExecuteGroupReport(int groupId, string procedureName, DateTime startTime, DateTime endTime);
    }

    public interface IReportWorkbookWriter
    {
        void Write(string filePath, IList<ReportDataSet> dataSets);
    }

    public interface IReportArchiveService
    {
        void CreateZip(string zipPath, IEnumerable<string> filePaths);
    }
}
