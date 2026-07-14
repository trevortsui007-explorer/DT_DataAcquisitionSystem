using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Infrastructure;
using System;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    public static class DataParserIocHelper
    {
        // 提供一个静态的解析器选择逻辑
        public static IDataParser GetParser(string ext)
        {
            var factory = new DataParserFactory(DefaultConverter);
            return factory.Create(ext);
        }

        // 提供一个静态的解析器对应的选项类

        public static ParserOptionsBase CreateOptions(string ext, string filePath, int headerRow = 1, int startRow = 2, bool hasExtFields = false, string extFields = null)
        {
            string normalizedExt = ext?.ToLower().TrimStart('.');

            if (normalizedExt == "csv" || normalizedExt == "txt")
            {
                return new CsvParserOptions
                {
                    FilePath = filePath,
                    HasExtFields = hasExtFields,
                    ExtFields = extFields,
                    Separator = ",",
                    Encoding = System.Text.Encoding.UTF8,
                    HeaderRow = headerRow,
                    StartRow = startRow
                };
            }

            if (normalizedExt == "xls" || normalizedExt == "xlsx")
            {
                return new ExcelParserOptions
                {
                    FilePath = filePath,
                    HasExtFields = hasExtFields,
                    ExtFields = extFields,
                    HeaderRow = headerRow,
                    StartRow = startRow
                };
            }

            return null;
        }

        private static object DefaultConverter(object val, Type t)
        {
            if (val == null) return null;
            return Convert.ChangeType(val, Nullable.GetUnderlyingType(t) ?? t);
        }
    }
}
