using DT_DataAcquisitionSystem.Common.Utilities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class FileDiscoveryService
    {
        private readonly IFileProviderFactory _factory;

        public FileDiscoveryService()
        {
            _factory = FileIocHelper.GetFileProviderFactory();
        }

        /// <summary>
        /// 获取文件夹下含有的文件名（用于处理文件名不规则，文件夹多文件情况）
        /// </summary>
        public async Task<List<string>> GetScanResultsAsync(string path, string extension, string user = "", string pass = "")
        {
            // 1. 获取 Provider (内部已自动判定 FTP 或 Local)
            var provider = _factory.Create(path, user, pass);

            // 2. 调用标准接口获取列表
            // 修正 searchPattern 格式，例如 ".txt" -> "*.txt"
            string pattern = extension.StartsWith("*") ? extension : "*" + extension;
            var files = await provider.GetFileNamesAsync(path, pattern);

            // 3. 业务处理：返回不带后缀的文件名 (如原 FileScanner 逻辑)
            return files.Select(Path.GetFileNameWithoutExtension).ToList();
        }
    }
}