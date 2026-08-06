using System;
using System.Collections.Generic;
using DT_DataAcquisitionSystem.Domain.Entities;
using Newtonsoft.Json.Linq;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class FileNameParseContext
    {
        public string FileName { get; set; }
        public string FullFilePath { get; set; }
        public JObject Options { get; set; }
    }

    public class FileNameParseResult
    {
        public FileNameParseResult()
        {
            Fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Warnings = new List<string>();
        }

        public Dictionary<string, object> Fields { get; set; }
        public IReadOnlyList<string> Warnings { get; set; }
    }

    public class FileNameParsingTestRequest
    {
        public string FileName { get; set; }
        public string FullFilePath { get; set; }
        public FileNameParsingDefinition FilenameParsing { get; set; }
    }

    public interface IFileNameMetadataParser
    {
        string ParserName { get; }
        FileNameParseResult Parse(FileNameParseContext context);
    }

    public interface IFileNameMetadataParserResolver
    {
        IFileNameMetadataParser Resolve(string parserName);
    }

    public interface IFileNameMetadataEnricher
    {
        IReadOnlyList<Dictionary<string, object>> Enrich(
            IEnumerable<Dictionary<string, object>> rows,
            string fileName,
            string fullFilePath,
            FileNameParsingDefinition definition);
    }
}
