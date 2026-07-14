using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using NPOI.SS.UserModel;
using NPOI.XSSF.Streaming;
using NPOI.XSSF.UserModel;

namespace DT_DataAcquisitionSystem.Infrastructure.Export
{
    public class NpoiReportWorkbookWriter : IReportWorkbookWriter
    {
        private const int MaxDataRowsPerSheet = 999999;

        public void Write(string filePath, IList<ReportDataSet> dataSets)
        {
            if (dataSets == null || dataSets.Count == 0)
            {
                throw new InvalidOperationException("没有可导出的数据集。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

            var workbook = new SXSSFWorkbook(new XSSFWorkbook(), 100);
            try
            {
                var headerStyle = workbook.CreateCellStyle();
                var headerFont = workbook.CreateFont();
                headerFont.IsBold = true;
                headerStyle.SetFont(headerFont);

                var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dataSet in dataSets)
                {
                    WriteDataSet(workbook, headerStyle, dataSet, usedSheetNames);
                }

                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    workbook.Write(stream);
                }
            }
            finally
            {
                workbook.Dispose();
            }
        }

        private void WriteDataSet(SXSSFWorkbook workbook, ICellStyle headerStyle, ReportDataSet dataSet, HashSet<string> usedSheetNames)
        {
            DataTable table = dataSet.Data ?? new DataTable();
            int totalRows = table.Rows.Count;
            int sheetCount = Math.Max(1, (int)Math.Ceiling(totalRows / (double)MaxDataRowsPerSheet));

            for (int partIndex = 0; partIndex < sheetCount; partIndex++)
            {
                string baseName = sheetCount == 1 ? dataSet.SheetName : $"{dataSet.SheetName}_{partIndex + 1}";
                string sheetName = CreateUniqueSheetName(baseName, usedSheetNames);
                var sheet = workbook.CreateSheet(sheetName);
                sheet.CreateFreezePane(0, 1);

                WriteHeader(sheet, headerStyle, table);

                int start = partIndex * MaxDataRowsPerSheet;
                int end = Math.Min(totalRows, start + MaxDataRowsPerSheet);
                for (int rowIndex = start; rowIndex < end; rowIndex++)
                {
                    var sourceRow = table.Rows[rowIndex];
                    var row = sheet.CreateRow(rowIndex - start + 1);
                    for (int colIndex = 0; colIndex < table.Columns.Count; colIndex++)
                    {
                        WriteCell(row.CreateCell(colIndex), sourceRow[colIndex]);
                    }
                }
            }
        }

        private static void WriteHeader(ISheet sheet, ICellStyle headerStyle, DataTable table)
        {
            var header = sheet.CreateRow(0);
            for (int i = 0; i < table.Columns.Count; i++)
            {
                var cell = header.CreateCell(i);
                cell.SetCellValue(table.Columns[i].ColumnName);
                cell.CellStyle = headerStyle;
            }
        }

        private static void WriteCell(ICell cell, object value)
        {
            if (value == null || value == DBNull.Value)
            {
                cell.SetCellValue(string.Empty);
                return;
            }

            if (value is DateTime dt)
            {
                cell.SetCellValue(dt.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            else if (value is int || value is long || value is short || value is decimal || value is double || value is float)
            {
                if (double.TryParse(Convert.ToString(value), out double number)) cell.SetCellValue(number);
                else cell.SetCellValue(Convert.ToString(value));
            }
            else if (value is bool boolean)
            {
                cell.SetCellValue(boolean);
            }
            else
            {
                cell.SetCellValue(Convert.ToString(value));
            }
        }

        private static string CreateUniqueSheetName(string requestedName, HashSet<string> usedSheetNames)
        {
            string clean = SanitizeSheetName(requestedName);
            string candidate = clean;
            int index = 1;
            while (usedSheetNames.Contains(candidate))
            {
                string suffix = "_" + index++;
                int maxBaseLength = Math.Max(1, 31 - suffix.Length);
                candidate = clean.Length > maxBaseLength ? clean.Substring(0, maxBaseLength) + suffix : clean + suffix;
            }
            usedSheetNames.Add(candidate);
            return candidate;
        }

        private static string SanitizeSheetName(string name)
        {
            string value = string.IsNullOrWhiteSpace(name) ? "Sheet" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars().Concat(new[] { '[', ']', '*', '?', '/', '\\', ':' }))
            {
                value = value.Replace(c, '_');
            }
            return value.Length > 31 ? value.Substring(0, 31) : value;
        }
    }
}



