using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using Newtonsoft.Json.Linq;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    public class FolderScanOptions
    {
        public bool Recursive { get; set; }
        public int MaxDepth { get; set; }
        public bool IncludeCurrentFolder { get; set; } = true;
    }

    public static class FolderScanOptionsUtil
    {
        public static FolderScanOptions FromConfig(AcquisitionConfig config)
        {
            var options = new FolderScanOptions();
            if (string.IsNullOrWhiteSpace(config?.ParserOptions)) return options;

            try
            {
                JObject root = JObject.Parse(config.ParserOptions);
                JToken folderScan = root["folderScan"] ?? root["FolderScan"];
                if (folderScan == null) return options;

                options.Recursive = ReadBool(folderScan, "recursive", "Recursive") ?? false;
                options.MaxDepth = Math.Max(ReadInt(folderScan, "maxDepth", "MaxDepth") ?? 0, 0);
                options.IncludeCurrentFolder = ReadBool(folderScan, "includeCurrentFolder", "IncludeCurrentFolder") ?? true;
            }
            catch
            {
                return options;
            }

            return options;
        }

        public static async Task<IEnumerable<string>> GetFilesAsync(
            IFileProvider provider,
            string folderPath,
            string searchPattern,
            FolderScanOptions options,
            CancellationToken ct)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            options = options ?? new FolderScanOptions();

            var files = await provider.GetFileNamesAsync(folderPath, searchPattern, options.Recursive, ct)
                .ConfigureAwait(false);

            return ApplyDepthFilter(folderPath, files, options).ToList();
        }

        private static IEnumerable<string> ApplyDepthFilter(
            string folderPath,
            IEnumerable<string> files,
            FolderScanOptions options)
        {
            foreach (string file in files ?? Enumerable.Empty<string>())
            {
                int depth = GetRelativeDepth(folderPath, file);

                if (!options.IncludeCurrentFolder && depth == 0)
                {
                    continue;
                }

                if (options.Recursive && options.MaxDepth > 0 && depth > options.MaxDepth)
                {
                    continue;
                }

                yield return file;
            }
        }

        private static int GetRelativeDepth(string folderPath, string filePath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(filePath))
            {
                return 0;
            }

            string normalizedRoot = NormalizePath(folderPath).TrimEnd('/', '\\');
            string normalizedFile = NormalizePath(filePath);
            if (!normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            string relative = normalizedFile.Substring(normalizedRoot.Length).TrimStart('/', '\\');
            string directory = Path.GetDirectoryName(relative);
            if (string.IsNullOrWhiteSpace(directory)) return 0;

            return directory.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static bool? ReadBool(JToken token, params string[] names)
        {
            JToken value = ReadToken(token, names);
            return value?.Type == JTokenType.Boolean ? value.Value<bool>() : (bool?)null;
        }

        private static int? ReadInt(JToken token, params string[] names)
        {
            JToken value = ReadToken(token, names);
            return value != null && int.TryParse(Convert.ToString(value), out int result) ? result : (int?)null;
        }

        private static JToken ReadToken(JToken token, params string[] names)
        {
            foreach (string name in names)
            {
                JToken value = token[name];
                if (value != null) return value;
            }

            return null;
        }
    }
}
