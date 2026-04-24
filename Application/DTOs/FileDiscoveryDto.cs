using System;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    /// <summary>
    /// 文件发现结果 DTO (按月分组)
    /// </summary>
    public class FileDiscoveryDto
    {
        public string MonthName { get; set; }        // 示例: "2025-01"
        public string FolderPath { get; set; }       // 解析后的物理路径
        public int FileCount => Files?.Count ?? 0;   // 统计该月文件数
        public List<FileEntryDto> Files { get; set; } = new List<FileEntryDto>();
    }

    /// <summary>
    /// 具体文件信息 DTO
    /// </summary>
    public class FileEntryDto
    {
        public string FileName { get; set; }         // 物理文件名
        public string FullPath { get; set; }         // 完整路径
        public DateTime DetectedDate { get; set; }   // 该文件对应的时间点
        public bool IsMissing { get; set; }          // 扩展：如果当天应该有文件却没找到，可以标记为缺失
    }
}