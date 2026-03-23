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

        [StringLength(1000)]
        public string FileName { get; set; }

        public int StartRow { get; set; }

        public int ProcessedRows { get; set; }

        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        [StringLength(50)]
        public string Status { get; set; }

        public string ErrorMessage { get; set; }
    }
}