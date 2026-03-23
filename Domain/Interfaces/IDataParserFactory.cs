namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface IDataParserFactory
    {
        /// <summary>
        /// 根据文件扩展名或数据类型获取对应的解析器
        /// </summary>
        /// <param name="fileExtension">例如 ".csv", ".xlsx"</param>
        IDataParser Create(string fileExtension);
    }
}