using System;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class AcquisitionTaskDto
    {
        public int Id { get; set; }

        public string TaskName { get; set; }

        public int TaskMode { get; set; }

        public string CronExpression { get; set; }

        public int IsEnabled { get; set; }

        public string Description { get; set; }

        public DateTime? CreateTime { get; set; }

        public DateTime? UpdateTime { get; set; }

        public int GroupCount { get; set; }

        public List<TaskAssociatedGroupDto> AssociatedGroups { get; set; } = new List<TaskAssociatedGroupDto>();
        public List<int> GroupIds { get; internal set; }
    }
}