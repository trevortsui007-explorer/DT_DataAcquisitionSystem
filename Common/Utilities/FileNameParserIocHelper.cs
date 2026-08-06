using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Infrastructure;
using Microsoft.Practices.Unity;
using Microsoft.Practices.Unity.Configuration;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    public static class FileNameParserIocHelper
    {
        private static readonly IFileNameMetadataParserResolver Resolver;
        private static readonly IFileNameMetadataEnricher Enricher;

        static FileNameParserIocHelper()
        {
            var container = new UnityContainer();
            var section = (UnityConfigurationSection)ConfigurationManager.GetSection("unity");
            section?.Configure(container, "FileNameParserContainer");

            var parsers = container.ResolveAll<IFileNameMetadataParser>().ToList();
            if (!parsers.Any(parser => parser.ParserName == "regex"))
                parsers.Add(new RegexFileNameMetadataParser());

            Resolver = new FileNameMetadataParserResolver(parsers);
            Enricher = new FileNameMetadataEnricher(Resolver);
        }

        public static FileNameParseResult Parse(
            string fileName,
            string fullFilePath,
            FileNameParsingDefinition definition)
        {
            var parser = Resolver.Resolve(definition?.ParserName);
            return parser.Parse(new FileNameParseContext
            {
                FileName = fileName,
                FullFilePath = fullFilePath,
                Options = definition.Options
            });
        }

        public static IReadOnlyList<Dictionary<string, object>> Enrich(
            IEnumerable<Dictionary<string, object>> rows,
            string fileName,
            string fullFilePath,
            FileNameParsingDefinition definition)
        {
            return Enricher.Enrich(rows, fileName, fullFilePath, definition);
        }
    }
}
