using System;
using System.Collections.Generic;
using System.Linq;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class FileNameMetadataParserResolver : IFileNameMetadataParserResolver
    {
        private readonly Dictionary<string, IFileNameMetadataParser> _parsers;

        public FileNameMetadataParserResolver(IEnumerable<IFileNameMetadataParser> parsers)
        {
            _parsers = (parsers ?? Enumerable.Empty<IFileNameMetadataParser>())
                .Where(parser => parser != null && !string.IsNullOrWhiteSpace(parser.ParserName))
                .GroupBy(parser => parser.ParserName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }

        public IFileNameMetadataParser Resolve(string parserName)
        {
            if (string.IsNullOrWhiteSpace(parserName))
                throw new InvalidOperationException("filenameParsing.parserName is required.");

            if (_parsers.TryGetValue(parserName.Trim(), out IFileNameMetadataParser parser))
                return parser;

            throw new InvalidOperationException($"File name metadata parser not found: {parserName}.");
        }
    }

    public class FileNameMetadataEnricher : IFileNameMetadataEnricher
    {
        private readonly IFileNameMetadataParserResolver _resolver;

        public FileNameMetadataEnricher(IFileNameMetadataParserResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public IReadOnlyList<Dictionary<string, object>> Enrich(
            IEnumerable<Dictionary<string, object>> rows,
            string fileName,
            string fullFilePath,
            FileNameParsingDefinition definition)
        {
            var rowList = (rows ?? Enumerable.Empty<Dictionary<string, object>>()).ToList();
            if (definition == null) return rowList;

            var parser = _resolver.Resolve(definition.ParserName);
            FileNameParseResult result;
            try
            {
                result = parser.Parse(new FileNameParseContext
                {
                    FileName = fileName,
                    FullFilePath = fullFilePath,
                    Options = definition.Options
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse file name '{fileName}' with parser '{definition.ParserName}': {ex.Message}",
                    ex);
            }

            var fields = result?.Fields ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rowList.Where(item => item != null))
            {
                foreach (var field in fields)
                {
                    if (!string.IsNullOrWhiteSpace(field.Key)) row[field.Key] = field.Value;
                }
            }

            return rowList;
        }
    }
}
