using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DT_DataAcquisitionSystem.Domain.Entities
{
    /// <summary>
    /// 采集明细日志表
    /// </summary>
    [Table("DA_AcquisitionLog", Schema = "dbo")]
    public class AcquisitionLogEntry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }

        /// <summary>
        /// 关联 DA_AcquisitionTaskLog 的 Id
        /// </summary>
        public string TaskLogId { get; set; }

        public int ConfigId { get; set; }

        public DateTime? BusinessDate { get; set; }

        [StringLength(1000)]
        public string FileName { get; set; }

        public string FullFilePath { get; set; }

        public int StartRow { get; set; }

        public int ProcessedRows { get; set; }

        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        [StringLength(50)]
        public string Status { get; set; }

        public string ErrorMessage { get; set; }
    }

    public class AcquisitionLogConfigGroup
    {
        public int ConfigId { get; set; }
        public string ConfigName { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int WarningFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ProcessedRows { get; set; }
    }

    public class AcquisitionLogConfigTaskGroup
    {
        public string TaskLogId { get; set; }
        public string TaskCode { get; set; }
        public string TriggerType { get; set; }
        public string TaskStatus { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int WarningFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ProcessedRows { get; set; }
    }

    public class AcquisitionLogConfigHistorySummary
    {
        public int ConfigId { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int WarningFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ProcessedRows { get; set; }
    }

    public class AcquisitionFileStateSummary
    {
        public int ConfigId { get; set; }
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int FailedFiles { get; set; }
        public int ProcessedRows { get; set; }
        public int NewFiles { get; set; }
    }

    /// <summary>
    /// 文件采集状态快照表：记录单个业务日期文件的当前行数和水位。
    /// </summary>
    [Table("DA_AcquisitionFileState", Schema = "dbo")]
    public class AcquisitionFileState
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public int ConfigId { get; set; }

        public DateTime BusinessDate { get; set; }

        [StringLength(500)]
        public string FileName { get; set; }

        [StringLength(1000)]
        public string FullPath { get; set; }

        public int DataRowCount { get; set; }

        public int LastStartRow { get; set; }

        public int LastProcessedRows { get; set; }

        [StringLength(50)]
        public string LastTaskLogId { get; set; }

        [StringLength(50)]
        public string LastStatus { get; set; }

        [StringLength(50)]
        public string LastUpdateSource { get; set; }

        public bool IsSealed { get; set; }

        public DateTime? SealTime { get; set; }

        public DateTime? LastScanTime { get; set; }

        public DateTime? LastWriteTime { get; set; }

        public DateTime? LastWriteTimeUtc { get; set; }

        public long? FileSize { get; set; }

        public DateTime CreateTime { get; set; }

        public DateTime UpdateTime { get; set; }
    }
}

