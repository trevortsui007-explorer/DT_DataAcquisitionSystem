using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DT_DataAcquisitionSystem.Application.Services;
using Newtonsoft.Json.Linq;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    public class RegexFileNameMetadataParser : IFileNameMetadataParser
    {
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

        public string ParserName => "regex";

        public FileNameParseResult Parse(FileNameParseContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            JObject options = context.Options ?? throw new InvalidOperationException("filenameParsing.options is required.");

            string pattern = GetString(options, "pattern");
            if (string.IsNullOrWhiteSpace(pattern))
                throw new InvalidOperationException("filenameParsing.options.pattern is required.");

            RegexOptions regexOptions = RegexOptions.CultureInvariant;
            if (GetBoolean(options, "ignoreCase")) regexOptions |= RegexOptions.IgnoreCase;

            string name = GetFileNameForMatching(context.FileName);
            Match match = new Regex(pattern, regexOptions, MatchTimeout).Match(name);
            if (!match.Success)
                throw new FormatException($"File name '{context.FileName}' does not match the configured pattern.");

            var result = new FileNameParseResult();
            JArray fields = options.GetValue("fields", StringComparison.OrdinalIgnoreCase) as JArray;
            if (fields == null) return result;

            foreach (JObject field in fields.OfType<JObject>())
            {
                string targetField = GetString(field, "field");
                string groupName = GetString(field, "group");
                bool required = GetBoolean(field, "required");
                if (string.IsNullOrWhiteSpace(targetField) || string.IsNullOrWhiteSpace(groupName))
                    throw new InvalidOperationException("Each filenameParsing field requires field and group.");

                Group group = match.Groups[groupName];
                string value = group.Success ? group.Value : null;
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (required) throw new FormatException($"Required file name field '{targetField}' is missing.");
                    result.Fields[targetField] = null;
                    continue;
                }

                result.Fields[targetField] = ConvertValue(value, field, targetField);
            }

            return result;
        }

        private static string GetFileNameForMatching(string fileName)
        {
            string name = Path.GetFileName(fileName ?? string.Empty);
            string extension = Path.GetExtension(name);
            bool looksLikeFileExtension = !string.IsNullOrWhiteSpace(extension)
                && extension.Skip(1).Any(char.IsLetter);

            return looksLikeFileExtension
                ? name.Substring(0, name.Length - extension.Length)
                : name;
        }

        private static object ConvertValue(string value, JObject field, string targetField)
        {
            string type = (GetString(field, "type") ?? "text").Trim().ToLowerInvariant();
            switch (type)
            {
                case "text":
                    return value.Trim();

                case "int":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                        return intValue;
                    break;

                case "decimal":
                    if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalValue))
                        return decimalValue;
                    break;

                case "date":
                    string format = GetString(field, "format");
                    DateTime dateValue;
                    bool parsed = string.IsNullOrWhiteSpace(format)
                        ? DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateValue)
                        : DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateValue);
                    if (parsed) return dateValue.Date;
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported file name field type '{type}' for '{targetField}'.");
            }

            throw new FormatException($"File name field '{targetField}' value '{value}' cannot be converted to {type}.");
        }

        private static string GetString(JObject value, string name)
        {
            return value?.GetValue(name, StringComparison.OrdinalIgnoreCase)?.Value<string>();
        }

        private static bool GetBoolean(JObject value, string name)
        {
            JToken token = value?.GetValue(name, StringComparison.OrdinalIgnoreCase);
            return token != null && token.Type != JTokenType.Null && token.Value<bool>();
        }
    }
}
