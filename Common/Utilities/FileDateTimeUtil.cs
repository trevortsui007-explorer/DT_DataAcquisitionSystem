using System;
using System.Text;
using System.Collections.Generic;
using DT_DataAcquisitionSystem.Domain.Entities;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    public static class FileDateTimeUtil
    {
        /// <summary>
        /// 使用指定的日期时间对象替换文件名或字符串中的日期时间占位符。
        /// 占位符包括 {yyyy}, {MM}, {M}, {dd}, {d}, {HH}, {mm}, {ss}, {fff}。
        /// 此版本使用 StringBuilder 优化了字符串替换操作，减少了中间字符串的创建。
        /// </summary>
        /// <param name="fileName">包含占位符的原始字符串（例如文件名）。</param>
        /// <param name="targetDate">用于替换的日期时间对象。</param>
        /// <returns>替换后的字符串。</returns>
        public static string GetDateTimeFromBrace(string fileName, DateTime targetDate)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return fileName;
            }

            // 预先计算所有替换值，避免重复计算 ToString()
            var replacementValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "{yyyy}", targetDate.ToString("yyyy") }, // 四位年份 (e.g., 2025)
                { "{MM}", targetDate.ToString("MM") },     // 两位月份 (e.g., 03)
                { "{M}", targetDate.Month.ToString() },    // 一位或两位月份 (e.g., 3)
                { "{dd}", targetDate.ToString("dd") },     // 两位日期 (e.g., 07)
                { "{d}", targetDate.Day.ToString() },      // 一位或两位日期 (e.g., 7)
                { "{HH}", targetDate.ToString("HH") },     // 两位小时 (24小时制) (e.g., 14)
                { "{mm}", targetDate.ToString("mm") },     // 两位分钟 (e.g., 05)
                { "{ss}", targetDate.ToString("ss") },     // 两位秒 (e.g., 30)
                { "{fff}", targetDate.ToString("fff") }    // 三位毫秒 (e.g., 500)
            };

            // 使用 StringBuilder 初始化，容量设为原始字符串长度，以减少内存重新分配
            var sb = new StringBuilder(fileName);

            // 遍历所有占位符并执行替换操作
            foreach (var kvp in replacementValues)
            {
                sb.Replace(kvp.Key, kvp.Value);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 传入采集配置，返回采集路径
        /// 如果fileName为空，会返回folderPath；非空返回完整路径
        /// </summary>
        public static string GetProcessedFilePath(AcquisitionConfig config, DateTime? processDate)
        {
            if (config == null) return null;

            // .NET 4.6.2 支持 ?? 运算符
            DateTime targetDate = processDate ?? DateTime.Now;

            string filePath = string.Empty;
            string fileName = string.Empty;

            // 1. 处理路径模式
            if (!string.IsNullOrEmpty(config.FilePathPattern))
            {
                filePath = FileDateTimeUtil.GetDateTimeFromBrace(config.FilePathPattern, targetDate);
            }

            // 2. 处理文件名模式
            if (!string.IsNullOrEmpty(config.FileNamePattern))
            {
                fileName = FileDateTimeUtil.GetDateTimeFromBrace(config.FileNamePattern, targetDate);
            }

            // 3. 逻辑判断：如果文件名模式为空，直接返回 filePath
            if (string.IsNullOrEmpty(fileName))
            {
                return filePath;
            }

            // 4. 如果两者都有，安全合并
            // Path.Combine 会自动处理路径分隔符（\）
            return System.IO.Path.Combine(filePath, fileName);
        }

        /// <summary>
        /// 传入采集配置，返回采集路径文件名
        /// </summary>
        public static string GetProcessedFileName(AcquisitionConfig config, DateTime? processDate)
        {
            if (config == null) return null;

            // .NET 4.6.2 支持 ?? 运算符
            DateTime targetDate = processDate ?? DateTime.Now;

            // 处理文件名模式
            string fileName = string.Empty;
            if (!string.IsNullOrEmpty(config.FileNamePattern))
            {
                fileName = FileDateTimeUtil.GetDateTimeFromBrace(config.FileNamePattern, targetDate);
            }

            return fileName;
        }
    }
}