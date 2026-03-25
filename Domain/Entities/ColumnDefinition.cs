using System;

namespace DT_DataAcquisitionSystem.Domain.Entities
{
    public class ColumnDefinition
    {
        public string ColumnName { get; set; }
        /// <summary>
        /// C# 类型，例如 typeof(string), typeof(int)
        /// </summary>
        public Type DataType { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool AllowNull { get; set; } = true;
        public int? MaxLength { get; set; } // 针对字符串类型，如 varchar(255)
    }
}