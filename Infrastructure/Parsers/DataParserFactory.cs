using System;
using System.Collections.Generic;
using System.IO;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    public class DataParserFactory : IDataParserFactory
    {
        private readonly Func<object, Type, object> _defaultConverter;

        // 使用字典存储扩展名与 Parser 类型的映射，方便以后扩展
        private static readonly Dictionary<string, Type> _parserMappings = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { ".csv", typeof(CsvStreamParser) },
            { ".xls", typeof(ExcelStreamParser) },
            { ".xlsx", typeof(ExcelStreamParser) },
            { ".txt", typeof(CsvStreamParser) }
        };

        public DataParserFactory(Func<object, Type, object> defaultConverter)
        {
            _defaultConverter = defaultConverter;
        }

        public IDataParser Create(string fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension))
                throw new ArgumentNullException(nameof(fileExtension));

            // 统一转小写进行匹配
            string ext = fileExtension.StartsWith(".") ? fileExtension.ToLower() : "." + fileExtension.ToLower();

            if (_parserMappings.TryGetValue(ext, out Type parserType))
            {
                // .NET 4.6.2 中，如果 Parser 构造函数需要参数，使用 Activator.CreateInstance
                // 这里的 _defaultConverter 是你在 CsvStreamParser 中定义的依赖
                return (IDataParser)Activator.CreateInstance(parserType, _defaultConverter);
            }

            throw new NotSupportedException($"不支持的文件格式: {fileExtension}");
        }
    }
}