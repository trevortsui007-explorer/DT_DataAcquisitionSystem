namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public interface IFileProviderFactory
    {
        /// <summary>
        /// 根据路径自动创建对应的 Provider，并可选择性注入凭据
        /// </summary>
        IFileProvider Create(string path, string username = null, string password = null);
    }
}