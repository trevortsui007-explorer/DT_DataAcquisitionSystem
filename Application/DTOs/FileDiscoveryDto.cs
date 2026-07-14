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
        public string DiscoveryMode { get; set; } = "calendar";
        public bool HasDayGranularity { get; set; } = true;
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
        public DateTime? LastWriteTime { get; set; }
        public long? FileSize { get; set; }
        public int? DataRowCount { get; set; }
        public int? LastStartRow { get; set; }
        public int? LastProcessedRows { get; set; }
        public string LastStatus { get; set; }
        public string LastUpdateSource { get; set; }
        public bool? IsSealed { get; set; }
        public DateTime? LastScanTime { get; set; }
        public DateTime? FileStateUpdateTime { get; set; }
    }

    /// <summary>
    /// 配置组单日巡检结果 DTO
    /// </summary>
    public class GroupFileDiscoveryDto
    {
        public int GroupId { get; set; }
        public DateTime Date { get; set; }
        public List<GroupFileDiscoveryItemDto> Items { get; set; } = new List<GroupFileDiscoveryItemDto>();
    }

    /// <summary>
    /// 配置组内单个设备的单日巡检结果 DTO
    /// </summary>
    public class GroupFileDiscoveryItemDto
    {
        public int ConfigId { get; set; }
        public string EqName { get; set; }
        public bool IsMissing { get; set; }
        public string FullFilePath { get; set; }
    }
}
