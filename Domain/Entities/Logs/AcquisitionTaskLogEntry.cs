using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DT_DataAcquisitionSystem.Domain.Entities
{
    /// <summary>
    /// 采集任务总日志表
    /// </summary>
    [Table("DA_AcquisitionTaskLog", Schema = "dbo")]
    public class AcquisitionTaskLogEntry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }

        public int TaskId { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        [StringLength(50)]
        public string Status { get; set; }

        public int TotalConfigs { get; set; }

        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }

        public int ProcessedCount { get; set; }

        public int Progress { get; set; }

        [StringLength(500)]
        public string Message { get; set; }
        public string TaskCode { get; set; }
        public string TriggerType { get; set; }
    }
}