using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    /// <summary>
    /// 数据映射工具类：用于根据配置动态转换 Dictionary 的键名（Key）
    /// </summary>
    public static class DataMapperUtil
    {
        /// <summary>
        /// 核心重载：支持传入 JSON 格式的映射字符串
        /// </summary>
        /// <param name="row">原始数据行</param>
        /// <param name="jsonFieldMapping">JSON 格式的映射表，例：{"OldName": "NewName"}</param>
        public static Dictionary<string, object> MapRow(Dictionary<string, object> row, string jsonFieldMapping)
        {
            Dictionary<string, string> fieldMapping = null;

            if (!string.IsNullOrWhiteSpace(jsonFieldMapping))
            {
                try
                {
                    fieldMapping = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonFieldMapping);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[Error] JSON 映射解析失败: {ex.Message}");
                }
            }

            return MapRow(row, fieldMapping);
        }

        /// <summary>
        /// 基础方法：将原始数据行根据映射字典进行字段名转换
        /// </summary>
        /// <param name="row">原始数据（Source）</param>
        /// <param name="fieldMapping">键名映射表 (Key: 原始名, Value: 目标名)</param>
        /// <returns>转换后的新字典</returns>
        public static Dictionary<string, object> MapRow(Dictionary<string, object> row, Dictionary<string, string> fieldMapping)
        {
            if (row == null) return new Dictionary<string, object>();

            var newRow = new Dictionary<string, object>(row.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in row)
            {
                string originalKey = kvp.Key;
                object value = kvp.Value;

                string targetKey = (fieldMapping != null && fieldMapping.TryGetValue(originalKey, out string mappedKey))
                                   ? mappedKey
                                   : originalKey;

                newRow[targetKey] = value;
            }

            return newRow;
        }
    }
}