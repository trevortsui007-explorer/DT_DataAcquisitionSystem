namespace DT_DataAcquisitionSystem.Domain.Entities
{
    public abstract class ParserOptionsBase
    {
        /// <summary>
        /// 表头所在行（1-based，默认第 1 行）
        /// </summary>
        public int HeaderRow { get; set; } = 1;

        /// <summary>
        /// 数据开始行（1-based，默认第 2 行）
        /// 用于断点续传：解析器应从此行开始读取
        /// </summary>
        public int StartRow { get; set; } = 2;

        /// <summary>
        /// 是否跳过空行
        /// </summary>
        public bool SkipEmptyLines { get; set; } = true;

        /// <summary>
        /// 是否自动 Trim 字段值
        /// </summary>
        public bool TrimFields { get; set; } = true;

        /// <summary>
        /// 是否添加额外的追溯字段
        /// </summary>
        public bool HasExtFields { get; set; } = false;

        /// <summary>
        /// 逗号分隔的扩展字段名。
        /// </summary>
        public string ExtFields { get; set; }

        /// <summary>
        /// 当前处理的文件全路径
        /// </summary>
        public string FilePath { get; set; }
    }
}
