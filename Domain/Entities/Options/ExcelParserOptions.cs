namespace DT_DataAcquisitionSystem.Domain.Entities
{
    /// <summary>
    /// Excel 解析配置
    /// </summary>
    public class ExcelParserOptions : ParserOptionsBase
    {
        /// <summary>
        /// 指定工作表名称（null = 读取第一个 Sheet）
        /// </summary>
        public string SheetName { get; set; }
    }
}