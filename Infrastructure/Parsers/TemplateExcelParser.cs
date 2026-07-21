using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using NPOI.SS.UserModel;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    public class TemplateExcelParser
    {
        public Task<List<Dictionary<string, object>>> ParseAsync(
            Stream stream,
            ExcelImportTemplate template,
            AcquisitionConfig config,
            string fileName,
            string fullFilePath,
            int minSourceRow,
            CancellationToken ct = default)
        {
            return Task.Run(() => Parse(stream, template, config, fileName, fullFilePath, minSourceRow), ct);
        }

        public List<Dictionary<string, object>> Parse(
            Stream stream,
            ExcelImportTemplate template,
            AcquisitionConfig config,
            string fileName,
            string fullFilePath,
            int minSourceRow)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            var definition = template.ParseDefinition();
            if (definition.DataRegion == null)
            {
                throw new InvalidOperationException($"Template {template.TemplateName} missing dataRegion.");
            }

            var workbook = WorkbookFactory.Create(stream);
            try
            {
                ISheet sheet = workbook.GetSheetAt(0);
                definition = ResolveDefinitionForWorkbook(sheet, template, definition);
                ValidateTemplateTitle(sheet, definition);

                var metadata = ReadMetadata(sheet, definition);
                var rows = new List<Dictionary<string, object>>();
                var carryValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                int emptyRows = 0;
                int candidateRows = 0;
                int startRow = definition.DataRegion.StartRow <= 0 ? config.StartRow : definition.DataRegion.StartRow;
                int effectiveMinSourceRow = IsAutoLineNoMetadataDefinition(definition)
                    ? startRow
                    : Math.Max(minSourceRow <= 0 ? startRow : minSourceRow, startRow);

                for (int rowNumber = startRow; rowNumber <= sheet.LastRowNum + 1; rowNumber++)
                {
                    if (MatchesKeywordStop(sheet, rowNumber, definition.DataRegion.StopRules))
                    {
                        break;
                    }

                    var emptyRule = GetEmptyRowsRule(definition.DataRegion.StopRules);
                    if (emptyRule != null && IsAllEmpty(sheet, rowNumber, emptyRule.Columns))
                    {
                        emptyRows++;
                        if (emptyRows >= Math.Max(emptyRule.Count, 1))
                        {
                            break;
                        }

                        continue;
                    }

                    emptyRows = 0;

                    if (ShouldSkip(sheet, rowNumber, definition.DataRegion.SkipRules))
                    {
                        continue;
                    }

                    object[] rawValues = ReadRawRowValues(sheet, rowNumber);
                    var row = new Dictionary<string, object>(metadata, StringComparer.OrdinalIgnoreCase)
                    {
                        ["SourceRow"] = rowNumber,
                        ["FileName"] = fileName,
                        ["FullFilePath"] = fullFilePath,
                        ["CreatedAt"] = DateTime.Now
                    };

                    ApplyFilenameFields(row, definition, fileName);

                    foreach (var column in definition.Columns ?? new List<ExcelTemplateColumn>())
                    {
                        object value = ReadColumnValue(sheet, rowNumber, column);
                        bool hasCurrentValue = HasRawValue(sheet, rowNumber, column.Column);

                        if (column.CarryForward)
                        {
                            if (hasCurrentValue)
                            {
                                carryValues[column.Field] = value;
                            }

                            row[column.Field] = hasCurrentValue
                                ? value
                                : carryValues.ContainsKey(column.Field)
                                    ? carryValues[column.Field]
                                    : GetDefaultCarryValue(column);
                        }
                        else
                        {
                            row[column.Field] = value;
                        }
                    }

                    ApplyTemplateSystemFields(row, definition, config, rowNumber, fullFilePath, rawValues);

                    candidateRows++;
                    if (rowNumber >= effectiveMinSourceRow)
                    {
                        rows.Add(row);
                    }
                }

                if (candidateRows == 0)
                {
                    throw new InvalidOperationException("Template structure mismatch. No valid detail rows were found; title may be missing but the data region could not be parsed.");
                }

                return rows;
            }
            finally
            {
                (workbook as IDisposable)?.Dispose();
            }
        }

        private static void ApplyTemplateSystemFields(Dictionary<string, object> row, ExcelTemplateDefinition definition, AcquisitionConfig config, int rowNumber, string fullFilePath, object[] rawValues)
        {
            if (definition?.SystemFields != null && definition.SystemFields.Count > 0)
            {
                ExtFieldValueBuilder.Apply(row, rowNumber, fullFilePath, string.Join(",", definition.SystemFields), rawValues);
            }

            if (definition?.RawFields != null)
            {
                foreach (var field in definition.RawFields)
                {
                    if (string.IsNullOrWhiteSpace(field.Key)) continue;
                    if (string.Equals(field.Value, "joinDetailRow", StringComparison.OrdinalIgnoreCase))
                    {
                        row[field.Key] = ExtFieldValueBuilder.BuildRowData(rawValues);
                    }
                }
            }

            if (definition?.FixedFields != null)
            {
                foreach (var field in definition.FixedFields)
                {
                    if (!string.IsNullOrWhiteSpace(field.Key)) row[field.Key] = field.Value;
                }
            }

            if (!string.IsNullOrWhiteSpace(config?.ExtFields))
            {
                ExtFieldValueBuilder.Apply(row, rowNumber, fullFilePath, config.ExtFields, rawValues);
            }
        }

        private static void ApplyFilenameFields(Dictionary<string, object> row, ExcelTemplateDefinition definition, string fileName)
        {
            if (row == null || definition?.FilenameFields == null) return;

            foreach (var item in definition.FilenameFields)
            {
                if (string.IsNullOrWhiteSpace(item.Key)) continue;

                if (string.Equals(item.Value, "firstDashPart", StringComparison.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
                    row[item.Key] = name.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                }
            }
        }

        private static ExcelTemplateDefinition ResolveDefinitionForWorkbook(ISheet sheet, ExcelImportTemplate template, ExcelTemplateDefinition definition)
        {
            if (sheet == null || !IsAutoLineWidthTemplate(template, definition)) return definition;

            if (TryDetectAutoLineNoMetadataHeader(sheet, out int headerRow))
            {
                return CreateAutoLineNoMetadataDefinition(headerRow);
            }

            return definition;
        }

        private static bool IsAutoLineWidthTemplate(ExcelImportTemplate template, ExcelTemplateDefinition definition)
        {
            string templateCode = template?.TemplateCode ?? string.Empty;
            string identityCode = definition?.Identity?.TemplateCode ?? string.Empty;

            return string.Equals(templateCode, "autoline-width", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(templateCode, "autoline-width-no-metadata", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(identityCode, "autoline-width", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(identityCode, "autoline-width-no-metadata", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAutoLineNoMetadataDefinition(ExcelTemplateDefinition definition)
        {
            return string.Equals(
                definition?.Identity?.TemplateCode,
                "autoline-width-no-metadata",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDetectAutoLineNoMetadataHeader(ISheet sheet, out int headerRow)
        {
            headerRow = 0;
            int maxRow = Math.Min(sheet.LastRowNum + 1, 10);
            string[] expectedHeaders =
            {
                "\u6d4b\u91cf\u9879\u76ee",
                "\u5224\u5b9a\u7ed3\u679c",
                "\u6d4b\u91cf\u503c",
                "\u6d4b\u91cf\u76ee\u6807\u503c",
                "\u6d4b\u91cf\u677f\u7f16\u53f7",
                "\u5355\u4f4d",
                "\u4e0a\u9650",
                "\u4e0b\u9650",
                "\u6d4b\u91cf\u65e5\u671f",
                "\u6d4b\u91cf\u65f6\u95f4",
                "\u6d4b\u91cf\u6a21\u5f0f"
            };
            string[] requiredHeaders =
            {
                "\u6d4b\u91cf\u9879\u76ee",
                "\u5224\u5b9a\u7ed3\u679c",
                "\u6d4b\u91cf\u503c",
                "\u6d4b\u91cf\u6a21\u5f0f"
            };

            int bestCount = 0;
            int bestRow = 0;

            for (int rowNumber = 1; rowNumber <= maxRow; rowNumber++)
            {
                var headers = ReadNormalizedHeaderTexts(sheet, rowNumber);
                if (!requiredHeaders.All(headers.Contains)) continue;

                int matchCount = expectedHeaders.Count(headers.Contains);
                if (matchCount > bestCount)
                {
                    bestCount = matchCount;
                    bestRow = rowNumber;
                }
            }

            if (bestCount < 8) return false;

            headerRow = bestRow;
            return true;
        }

        private static HashSet<string> ReadNormalizedHeaderTexts(ISheet sheet, int rowNumber)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IRow row = sheet.GetRow(rowNumber - 1);
            if (row == null || row.LastCellNum <= 0) return result;

            for (int index = 0; index < row.LastCellNum; index++)
            {
                string text = NormalizeHeaderText(GetCellValue(row.GetCell(index, MissingCellPolicy.RETURN_BLANK_AS_NULL)));
                if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
            }

            return result;
        }

        private static string NormalizeHeaderText(object value)
        {
            string text = NormalizeText(value);
            return Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
        }

        private static ExcelTemplateDefinition CreateAutoLineNoMetadataDefinition(int headerRow)
        {
            int startRow = Math.Max(1, headerRow) + 1;

            return new ExcelTemplateDefinition
            {
                Identity = new ExcelTemplateIdentity
                {
                    TemplateCode = "autoline-width-no-metadata",
                    TemplateName = "\u5185\u5c42\u81ea\u52a8\u7ebf\u5bbd\u673a-\u65e0\u5143\u6570\u636e"
                },
                Metadata = new List<ExcelTemplateMetadataField>(),
                DataRegion = new ExcelTemplateDataRegion
                {
                    StartRow = startRow,
                    StopRules = new List<ExcelTemplateRule>
                    {
                        new ExcelTemplateRule
                        {
                            Type = "emptyRows",
                            Count = 2,
                            Columns = new List<string> { "A", "B", "C", "D", "K" }
                        }
                    },
                    SkipRules = new List<ExcelTemplateRule>
                    {
                        new ExcelTemplateRule
                        {
                            Type = "allEmpty",
                            Columns = new List<string> { "A", "B", "C", "D", "K" }
                        }
                    }
                },
                Columns = new List<ExcelTemplateColumn>
                {
                    new ExcelTemplateColumn { Field = "Measure_item", Column = "A", Type = "string" },
                    new ExcelTemplateColumn { Field = "RESULT", Column = "B", Type = "string" },
                    new ExcelTemplateColumn { Field = "measured_value", Column = "C", Type = "string" },
                    new ExcelTemplateColumn { Field = "Specification", Column = "D", Type = "string" },
                    new ExcelTemplateColumn { Field = "SERIAL_NO", Column = "E", Type = "int" },
                    new ExcelTemplateColumn { Field = "MAX_VALUE", Column = "G", Type = "string" },
                    new ExcelTemplateColumn { Field = "MIN_VALUE", Column = "H", Type = "string" },
                    new ExcelTemplateColumn { Field = "Measure_type", Column = "K", Type = "string" }
                },
                SystemFields = new List<string>
                {
                    "Id(\u96ea\u82b1\u7b97\u6cd5)",
                    "fileName",
                    "row",
                    "RowData(str)",
                    "CreateDt",
                    "excelname",
                    "TH",
                    "SKYZ"
                },
                FilenameFields = new Dictionary<string, string>
                {
                    ["prodno_Layer"] = "firstDashPart"
                },
                RawFields = new Dictionary<string, string>
                {
                    ["str"] = "joinDetailRow"
                },
                FixedFields = new Dictionary<string, object>
                {
                    ["TH"] = "0",
                    ["SKYZ"] = "0"
                }
            };
        }

        private static void ValidateTemplateTitle(ISheet sheet, ExcelTemplateDefinition definition)
        {
            if (definition.Identity == null || string.IsNullOrWhiteSpace(definition.Identity.TitleContains))
            {
                return;
            }

            string title = Convert.ToString(GetCellValue(sheet, definition.Identity.TitleCell)) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title) || !title.Contains(definition.Identity.TitleContains))
            {
                return;
            }
        }

        private static Dictionary<string, object> ReadMetadata(ISheet sheet, ExcelTemplateDefinition definition)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in definition.Metadata ?? new List<ExcelTemplateMetadataField>())
            {
                object rawValue = GetMetadataRawValue(sheet, item);
                result[item.Field] = ParseMetadataValue(rawValue, item);
            }

            return result;
        }

        private static object GetMetadataRawValue(ISheet sheet, ExcelTemplateMetadataField item)
        {
            if (item != null &&
                string.Equals(item.Parser, "checkedOption", StringComparison.OrdinalIgnoreCase) &&
                TryParseCellAddress(item.Source, out int rowNumber, out string column))
            {
                return ReadCheckedOptionText(sheet, rowNumber, column, item.Options);
            }

            return GetCellValue(sheet, item?.Source);
        }

        private static string ReadCheckedOptionText(ISheet sheet, int rowNumber, string startColumn, IEnumerable<string> options)
        {
            int startIndex = ColumnToIndex(startColumn);
            if (rowNumber <= 0 || startIndex < 0) return string.Empty;

            IRow row = sheet.GetRow(rowNumber - 1);
            int scanSpan = Math.Max(((options ?? Enumerable.Empty<string>()).Count() + 1) * 4, 12);
            int lastIndex = row?.LastCellNum > 0 ? row.LastCellNum - 1 : startIndex + scanSpan;
            int endIndex = Math.Min(lastIndex, startIndex + scanSpan);
            var values = new List<string>();

            for (int index = startIndex; index <= endIndex; index++)
            {
                string text = NormalizeText(GetCellValue(sheet, rowNumber, IndexToColumn(index)));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }
            }

            return string.Join(" ", values);
        }

        private static object ParseMetadataValue(object rawValue, ExcelTemplateMetadataField item)
        {
            string parser = item.Parser ?? string.Empty;

            if (parser.Equals("dateFromChineseText", StringComparison.OrdinalIgnoreCase))
            {
                return ParseChineseDate(Convert.ToString(rawValue));
            }

            if (parser.Equals("checkedOption", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCheckedOption(Convert.ToString(rawValue), item.Options);
            }

            return NormalizeText(rawValue);
        }

        private static object ReadColumnValue(ISheet sheet, int rowNumber, ExcelTemplateColumn column)
        {
            string type = column.Type ?? "string";

            if (type.Equals("judgmentFromColumns", StringComparison.OrdinalIgnoreCase))
            {
                if (IsChecked(GetCellValue(sheet, rowNumber, column.AccColumn))) return "ACC";
                if (IsChecked(GetCellValue(sheet, rowNumber, column.RejColumn))) return "REJ";
                return string.Empty;
            }

            object rawValue = GetCellValue(sheet, rowNumber, column.Column);

            if (type.Equals("decimal", StringComparison.OrdinalIgnoreCase))
            {
                return ParseDecimal(rawValue);
            }

            if (type.Equals("int", StringComparison.OrdinalIgnoreCase))
            {
                decimal? value = ParseDecimal(rawValue);
                return value.HasValue ? (object)Convert.ToInt32(Math.Truncate(value.Value)) : null;
            }

            if (type.Equals("booleanCheck", StringComparison.OrdinalIgnoreCase))
            {
                return IsChecked(rawValue);
            }

            return NormalizeText(rawValue);
        }

        private static bool MatchesKeywordStop(ISheet sheet, int rowNumber, IEnumerable<ExcelTemplateRule> rules)
        {
            foreach (var rule in rules ?? Enumerable.Empty<ExcelTemplateRule>())
            {
                if (!string.Equals(rule.Type, "keyword", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var column in rule.Columns ?? new List<string>())
                {
                    string text = NormalizeText(GetCellValue(sheet, rowNumber, column));
                    if ((rule.Keywords ?? new List<string>()).Any(keyword => text.Contains(keyword)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static ExcelTemplateRule GetEmptyRowsRule(IEnumerable<ExcelTemplateRule> rules)
        {
            return (rules ?? Enumerable.Empty<ExcelTemplateRule>())
                .FirstOrDefault(x => string.Equals(x.Type, "emptyRows", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ShouldSkip(ISheet sheet, int rowNumber, IEnumerable<ExcelTemplateRule> rules)
        {
            foreach (var rule in rules ?? Enumerable.Empty<ExcelTemplateRule>())
            {
                if (string.Equals(rule.Type, "allEmpty", StringComparison.OrdinalIgnoreCase) &&
                    IsAllEmpty(sheet, rowNumber, rule.Columns))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAllEmpty(ISheet sheet, int rowNumber, IEnumerable<string> columns)
        {
            return (columns ?? Enumerable.Empty<string>())
                .All(column => !HasRawValue(sheet, rowNumber, column));
        }

        private static object GetDefaultCarryValue(ExcelTemplateColumn column)
        {
            if (string.Equals(column.Type, "booleanCheck", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }

        private static object[] ReadRawRowValues(ISheet sheet, int rowNumber)
        {
            IRow row = sheet.GetRow(rowNumber - 1);
            if (row == null || row.LastCellNum <= 0) return Array.Empty<object>();

            object[] values = new object[row.LastCellNum];
            for (int i = 0; i < row.LastCellNum; i++)
            {
                values[i] = GetCellValue(row.GetCell(i, MissingCellPolicy.CREATE_NULL_AS_BLANK));
            }

            return values;
        }

        private static object GetCellValue(ISheet sheet, string address)
        {
            if (!TryParseCellAddress(address, out int rowNumber, out string column))
            {
                return null;
            }

            return GetCellValue(sheet, rowNumber, column);
        }

        private static object GetCellValue(ISheet sheet, int rowNumber, string column)
        {
            int columnIndex = ColumnToIndex(column);
            if (rowNumber <= 0 || columnIndex < 0) return null;

            IRow row = sheet.GetRow(rowNumber - 1);
            return GetCellValue(row?.GetCell(columnIndex, MissingCellPolicy.RETURN_BLANK_AS_NULL));
        }

        private static object GetCellValue(ICell cell)
        {
            if (cell == null) return null;

            switch (cell.CellType)
            {
                case CellType.Numeric:
                    return DateUtil.IsCellDateFormatted(cell) ? (object)cell.DateCellValue : cell.NumericCellValue;
                case CellType.Boolean:
                    return cell.BooleanCellValue;
                case CellType.String:
                    return cell.StringCellValue;
                case CellType.Formula:
                    try
                    {
                        return cell.NumericCellValue;
                    }
                    catch
                    {
                        return cell.ToString();
                    }
                default:
                    return cell.ToString();
            }
        }

        private static bool HasRawValue(ISheet sheet, int rowNumber, string column)
        {
            object value = GetCellValue(sheet, rowNumber, column);
            return !string.IsNullOrWhiteSpace(Convert.ToString(value));
        }

        private static bool TryParseCellAddress(string address, out int rowNumber, out string column)
        {
            rowNumber = 0;
            column = string.Empty;

            if (string.IsNullOrWhiteSpace(address)) return false;

            Match match = Regex.Match(address.Trim(), "^([A-Za-z]+)(\\d+)$");
            if (!match.Success) return false;

            column = match.Groups[1].Value;
            return int.TryParse(match.Groups[2].Value, out rowNumber);
        }

        private static int ColumnToIndex(string column)
        {
            if (string.IsNullOrWhiteSpace(column)) return -1;

            int index = 0;
            string text = column.Trim().ToUpperInvariant();
            for (int i = 0; i < text.Length; i++)
            {
                index = index * 26 + (text[i] - 'A' + 1);
            }

            return index - 1;
        }

        private static string IndexToColumn(int index)
        {
            if (index < 0) return string.Empty;

            int value = index + 1;
            var chars = new Stack<char>();
            while (value > 0)
            {
                value--;
                chars.Push((char)('A' + value % 26));
                value /= 26;
            }

            return new string(chars.ToArray());
        }

        private static string NormalizeText(object value)
        {
            return Convert.ToString(value)?.Trim() ?? string.Empty;
        }

        private static bool IsChecked(object value)
        {
            string text = NormalizeText(value);
            return GetCheckMarkers().Any(marker => text.Contains(marker));
        }

        private static string ParseCheckedOption(string text, IEnumerable<string> options)
        {
            string normalized = NormalizeText(text);
            string[] markers = GetCheckMarkers();
            var optionList = (options ?? Enumerable.Empty<string>())
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .ToList();

            foreach (string option in optionList)
            {
                string escapedOption = Regex.Escape(option);
                if (markers.Any(marker => Regex.IsMatch(normalized, Regex.Escape(marker) + "\\s*" + escapedOption)))
                {
                    return option;
                }
            }

            int markerIndex = markers
                .Select(marker => normalized.IndexOf(marker, StringComparison.Ordinal))
                .Where(index => index >= 0)
                .DefaultIfEmpty(-1)
                .Min();
            if (markerIndex >= 0)
            {
                string checkedPart = normalized.Substring(markerIndex);
                return optionList.FirstOrDefault(option => checkedPart.Contains(option)) ?? string.Empty;
            }

            return string.Empty;
        }

        private static string[] GetCheckMarkers()
        {
            return new[]
            {
                "\u221a",
                "\u2713",
                "\u2714",
                "\u25a0",
                "\u2588",
                "\u25cf",
                "\u2611"
            };
        }

        private static DateTime? ParseChineseDate(string text)
        {
            Match match = Regex.Match(NormalizeText(text), "(\\d{4})\\D+(\\d{1,2})\\D+(\\d{1,2})");
            if (!match.Success) return null;

            string value = $"{match.Groups[1].Value}-{match.Groups[2].Value.PadLeft(2, '0')}-{match.Groups[3].Value.PadLeft(2, '0')}";
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return date.Date;
            }

            return null;
        }

        private static decimal? ParseDecimal(object rawValue)
        {
            if (rawValue == null) return null;
            if (rawValue is decimal decimalValue) return decimalValue;
            if (rawValue is double doubleValue) return Convert.ToDecimal(doubleValue);
            if (rawValue is int intValue) return intValue;

            string text = NormalizeText(rawValue).Replace(",", string.Empty);
            if (string.IsNullOrWhiteSpace(text)) return null;

            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)
                ? (decimal?)value
                : null;
        }
    }
}
