using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Globalization;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    public abstract class BaseStreamParser
    {
        protected readonly Func<object, Type, object> _converter;

        // 反射缓存：避免频繁 GetProperties 损耗性能
        private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> _propertyCache
            = new ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>>();

        protected BaseStreamParser(Func<object, Type, object> converter)
        {
            _converter = converter;
        }

        /// <summary>
        /// 通用映射逻辑：支持 Dictionary 和 强类型实体
        /// </summary>
        protected T MapToEntity<T>(string[] headers, object[] values, int rowIndex, bool hasExt, string filePath, string extFields = null, object[] rawValues = null) where T : class, new()
        {
            // 情况 A：处理 Dictionary<string, object>
            if (typeof(T) == typeof(Dictionary<string, object>))
            {
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                int minLen = Math.Min(headers.Length, values.Length);
                for (int i = 0; i < minLen; i++)
                {
                    dict[headers[i]] = values[i];
                }

                if (hasExt)
                {
                    ApplyExtFields(dict, rowIndex, filePath, extFields, rawValues ?? values);
                }
                return dict as T;
            }

            // 情况 B：处理强类型实体
            T entity = new T();
            var propMap = GetPropertyMap<T>();
            int minLength = Math.Min(headers.Length, values.Length);

            for (int i = 0; i < minLength; i++)
            {
                if (propMap.TryGetValue(headers[i], out var prop))
                {
                    SetPropertyValue(entity, prop, values[i]);
                }
            }

            if (hasExt)
            {
                var extValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                ApplyExtFields(extValues, rowIndex, filePath, extFields, rawValues ?? values);
                foreach (var kvp in extValues)
                {
                    if (propMap.TryGetValue(kvp.Key, out var prop))
                    {
                        SetPropertyValue(entity, prop, kvp.Value);
                    }
                }
            }
            return entity;
        }

        private void ApplyExtFields(Dictionary<string, object> row, int rowIndex, string filePath, string extFields, object[] rawValues)
        {
            ExtFieldValueBuilder.Apply(row, rowIndex, filePath, extFields, rawValues);
        }
        private void SetPropertyValue(object entity, PropertyInfo prop, object rawValue)
        {
            object convertedValue = _converter(rawValue, prop.PropertyType);

            // 处理 Nullable 和 ValueType 的赋值安全
            if (convertedValue != null || !prop.PropertyType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) != null)
            {
                prop.SetValue(entity, convertedValue == DBNull.Value ? null : convertedValue);
            }
        }

        private Dictionary<string, PropertyInfo> GetPropertyMap<T>()
        {
            return _propertyCache.GetOrAdd(typeof(T), t =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Where(p => p.CanWrite)
                 .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase));
        }
    }
    internal static class ExtFieldValueBuilder
    {
        private static readonly object SnowflakeLock = new object();
        private static long LastSnowflakeTimestamp = -1L;
        private static long SnowflakeSequence = 0L;
        private const long SnowflakeEpoch = 1577836800000L;
        private const long SnowflakeWorkerId = 1L;
        private const long SnowflakeSequenceMask = 4095L;

        public static void Apply(IDictionary<string, object> row, int rowIndex, string filePath, string extFields, object[] rawValues = null)
        {
            if (row == null) return;

            var fields = ParseExtFields(extFields).ToList();
            if (fields.Count == 0)
            {
                fields.Add("row");
                fields.Add("fullFilePath");
            }

            DateTime createTime = DateTime.Now;
            foreach (var field in fields)
            {
                string functionName;
                string argument;
                if (TryParseFunction(field, out functionName, out argument))
                {
                    ApplyFunction(row, functionName, argument, rawValues);
                    continue;
                }

                string normalized = NormalizeExtField(field);
                if (normalized == "row" || normalized == "sourcerow")
                {
                    row[field] = rowIndex;
                }
                else if (normalized == "filename")
                {
                    row[field] = Path.GetFileName(filePath);
                }
                else if (normalized == "fullfilepath")
                {
                    row[field] = filePath;
                }
                else if (normalized == "excelname")
                {
                    row[field] = Path.GetFileNameWithoutExtension(filePath);
                }
                else if (normalized == "createdat" || normalized == "createdt")
                {
                    row[field] = createTime;
                }
                else if (normalized == "th" || normalized == "skyz")
                {
                    row[field] = "0";
                }
            }
        }

        public static string BuildRowData(object[] values)
        {
            if (values == null || values.Length == 0) return string.Empty;

            int lastIndex = values.Length - 1;
            while (lastIndex >= 0 && IsBlank(values[lastIndex]))
            {
                lastIndex--;
            }

            if (lastIndex < 0) return string.Empty;

            return string.Join(",", values.Take(lastIndex + 1).Select(FormatRowValue));
        }

        private static void ApplyFunction(IDictionary<string, object> row, string functionName, string argument, object[] rawValues)
        {
            string normalized = NormalizeExtField(functionName);
            if (normalized == "id")
            {
                row["Id"] = IsSnowflake(argument)
                    ? NextSnowflakeId().ToString(CultureInfo.InvariantCulture)
                    : Guid.NewGuid().ToString("D");
            }
            else if (normalized == "rowdata" && !string.IsNullOrWhiteSpace(argument))
            {
                row[argument.Trim()] = BuildRowData(rawValues);
            }
        }

        private static IEnumerable<string> ParseExtFields(string extFields)
        {
            if (string.IsNullOrWhiteSpace(extFields)) yield break;

            foreach (var field in extFields.Split(','))
            {
                string value = field?.Trim();
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
            }
        }

        private static bool TryParseFunction(string field, out string functionName, out string argument)
        {
            functionName = null;
            argument = null;

            if (string.IsNullOrWhiteSpace(field)) return false;

            string text = field.Trim();
            int openIndex = text.IndexOf('(');
            if (openIndex <= 0 || !text.EndsWith(")", StringComparison.Ordinal)) return false;

            functionName = text.Substring(0, openIndex).Trim();
            argument = text.Substring(openIndex + 1, text.Length - openIndex - 2).Trim();
            return functionName.Length > 0;
        }

        private static bool IsSnowflake(string algorithm)
        {
            if (string.IsNullOrWhiteSpace(algorithm)) return false;

            return algorithm.IndexOf("雪花", StringComparison.OrdinalIgnoreCase) >= 0
                || algorithm.IndexOf("snowflake", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeExtField(string field)
        {
            return (field ?? string.Empty)
                .Trim()
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static bool IsBlank(object value)
        {
            return value == null || value == DBNull.Value || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static string FormatRowValue(object value)
        {
            if (value == null || value == DBNull.Value) return string.Empty;
            if (value is DateTime dateTime) return dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long NextSnowflakeId()
        {
            lock (SnowflakeLock)
            {
                long timestamp = CurrentTimeMillis();
                if (timestamp < LastSnowflakeTimestamp)
                {
                    timestamp = LastSnowflakeTimestamp;
                }

                if (timestamp == LastSnowflakeTimestamp)
                {
                    SnowflakeSequence = (SnowflakeSequence + 1) & SnowflakeSequenceMask;
                    if (SnowflakeSequence == 0)
                    {
                        while (timestamp <= LastSnowflakeTimestamp)
                        {
                            timestamp = CurrentTimeMillis();
                        }
                    }
                }
                else
                {
                    SnowflakeSequence = 0L;
                }

                LastSnowflakeTimestamp = timestamp;
                return ((timestamp - SnowflakeEpoch) << 22) | (SnowflakeWorkerId << 12) | SnowflakeSequence;
            }
        }

        private static long CurrentTimeMillis()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }
    }
}
