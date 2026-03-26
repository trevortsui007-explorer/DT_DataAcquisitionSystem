using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Application.DTOs
{
    public class CreateTableRequest
    {
        public string TableName { get; set; }
        public List<ApiColumnDefinition> Columns { get; set; }
    }

    public class ApiColumnDefinition
    {
        public string ColumnName { get; set; }
        /// <summary>
        /// 接收前端传来的类型字符串，如 "string", "int", "datetime"
        /// </summary>
        public string DataType { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool AllowNull { get; set; } = true;
        public int? MaxLength { get; set; }
    }

    public class TableColumnInfo
    {
        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public bool AllowDBNull { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsPrimaryKey { get; set; }
        public int MaxLength { get; set; }
        public string DefaultValue { get; set; }
    }
}