using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Common.Utilities;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class DataPreviewService
    {
        private readonly IFileProviderFactory _fileFactory;

        public DataPreviewService(IFileProviderFactory fileFactory)
        {
            _fileFactory = fileFactory;
        }

        public async Task<List<Dictionary<string, object>>> GetFilePreview(string path, int top = 10, CancellationToken ct = default)
        {
            // 1. 获取文件提供者（支持本地/FTP）
            var provider = _fileFactory.Create(path, null, null);

            if (!provider.Exists(path))
                throw new FileNotFoundException("未找到指定文件", path);

            // 2. 获取流
            using (var stream = await provider.GetFileStreamAsync(path, ct))
            {
                // 3. 根据后缀获取解析器
                string ext = Path.GetExtension(path);
                var parser = DataParserIocHelper.GetParser(ext);

                // 4. 解析数据（这里我们使用 Dictionary 这种通用格式进行预览）
                var fullData = await parser.ParseAsync<Dictionary<string, object>>(stream, null, ct);

                // 5. 返回前 N 行
                return fullData.Take(top).ToList();
            }
        }
    }
}