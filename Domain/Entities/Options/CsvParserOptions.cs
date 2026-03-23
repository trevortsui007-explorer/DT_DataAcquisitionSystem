using System.Text;

namespace DT_DataAcquisitionSystem.Domain.Entities
{
    /// <summary>
    /// CSV 解析器配置选项（可扩展）
    /// </summary>
    public class CsvParserOptions : ParserOptionsBase
    {
        /// <summary>
        /// 分隔符（默认逗号）
        /// </summary>
        public string Separator { get; set; } = ",";

        /// <summary>
        /// 文件编码（默认 Encoding.Default）
        /// </summary>
        public Encoding Encoding { get; set; } = Encoding.Default;

        /// <summary>
        /// 读取缓冲区大小（默认 4096）
        /// </summary>
        public int BufferSize { get; set; } = 4096;
    }
}