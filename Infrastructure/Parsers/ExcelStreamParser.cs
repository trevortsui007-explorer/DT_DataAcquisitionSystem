using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NPOI.SS.UserModel;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    public class ExcelStreamParser : BaseStreamParser, IDataParser
    {
        public ExcelStreamParser(Func<object, Type, object> converter) : base(converter) { }

        public List<T> Parse<T>(Stream stream, object optionsObj = null) where T : class, new()
        {
            var opt = optionsObj as ExcelParserOptions ?? new ExcelParserOptions();
            var result = new List<T>();

            // NPOI 自动识别 .xls 和 .xlsx
            var workbook = WorkbookFactory.Create(stream);
            try
            {
                ISheet sheet = GetSheet(workbook, opt.SheetName);
                if (sheet == null) return result;

                string[] headers = ReadHeaders(sheet, opt);

                // NPOI 的 LastRowNum 是 0-based index
                // r = 起始数据行 - 1 (转换为 NPOI 的 0 基索引)
                for (int r = opt.StartRow - 1; r <= sheet.LastRowNum; r++)
                {
                    IRow row = sheet.GetRow(r);
                    if (row == null) continue;

                    if (opt.SkipEmptyLines && IsEmptyRow(row)) continue;

                    object[] fieldValues = ReadRowValues(row, headers.Length, opt);
                    object[] rawValues = ReadRowValues(row, Math.Max(headers.Length, row.LastCellNum), opt);

                    // 重点：r + 1 还原为 Excel 左侧显示的物理行号，传入父类映射
                    result.Add(MapToEntity<T>(headers, fieldValues, r + 1, opt.HasExtFields, opt.FilePath, opt.ExtFields, rawValues));
                }
            }
            finally
            {
                 // 手动进行释放检查
                 if (workbook != null)
                 {
                    // 强制转换为 IDisposable 进行释放
                    (workbook as IDisposable)?.Dispose();
                 }
            }
            return result;
        }

        public async Task<List<T>> ParseAsync<T>(Stream stream, object optionsObj = null, CancellationToken ct = default) where T : class, new()
        {
            // NPOI 本身不支持真正的原生 Async，使用 Task.Run 配合包装
            return await Task.Run(() => Parse<T>(stream, optionsObj), ct);
        }

        #region --- NPOI 内部私有工具 ---

        private ISheet GetSheet(IWorkbook workbook, string sheetName)
        {
            return string.IsNullOrEmpty(sheetName) ? workbook.GetSheetAt(0) : workbook.GetSheet(sheetName);
        }

        private string[] ReadHeaders(ISheet sheet, ExcelParserOptions options)
        {
            IRow headerRow = sheet.GetRow(options.HeaderRow - 1);
            if (headerRow == null) return Array.Empty<string>();

            int colCount = headerRow.LastCellNum;
            string[] headers = new string[colCount];
            for (int i = 0; i < colCount; i++)
            {
                ICell cell = headerRow.GetCell(i);
                headers[i] = (cell?.ToString() ?? "").Trim();
            }
            return headers;
        }

        private bool IsEmptyRow(IRow row)
        {
            if (row == null) return true;
            for (int i = 0; i < row.LastCellNum; i++)
            {
                ICell cell = row.GetCell(i);
                if (cell != null && cell.CellType != CellType.Blank && !string.IsNullOrWhiteSpace(cell.ToString()))
                    return false;
            }
            return true;
        }

        private object[] ReadRowValues(IRow row, int headerCount, ExcelParserOptions options)
        {
            object[] values = new object[headerCount];
            for (int i = 0; i < headerCount; i++)
            {
                ICell cell = row.GetCell(i, MissingCellPolicy.CREATE_NULL_AS_BLANK);
                object val = GetCellValue(cell);

                if (options.TrimFields && val is string s)
                    val = s.Trim();

                values[i] = val;
            }
            return values;
        }

        private object GetCellValue(ICell cell)
        {
            if (cell == null) return null;
            switch (cell.CellType)
            {
                case CellType.Numeric:
                    if (DateUtil.IsCellDateFormatted(cell))
                    {
                        return cell.DateCellValue; // 返回 DateTime (会被你的 _converter 转成数据库需要的格式)
                    }
                    return cell.NumericCellValue;  // 返回 double
                case CellType.Boolean:
                    return cell.BooleanCellValue;
                case CellType.String:
                    return cell.StringCellValue;
                case CellType.Formula:
                    try { return cell.NumericCellValue; } // 尝试获取公式结果
                    catch { return cell.ToString(); }
                default:
                    return cell.ToString();
            }
        }

        #endregion
    }
}
