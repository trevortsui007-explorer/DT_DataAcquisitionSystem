using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Domain.Interfaces
{
    public class FileMetadata
    {
        public System.DateTime? LastWriteTime { get; set; }
        public System.DateTime? LastWriteTimeUtc { get; set; }
        public long? Length { get; set; }
    }

    public interface IFileProvider
    {
        // 0. 路由判定（用于消除 Factory 中的 if/else）
        bool CanHandle(string path);

        // 1. 判断是否存在
        bool Exists(string filePath);

        // 2. 异步获取流
        Task<Stream> GetFileStreamAsync(string filePath, CancellationToken cancellationToken = default);

        // 2.1 获取文件元数据
        Task<FileMetadata> GetFileMetadataAsync(string filePath, CancellationToken cancellationToken = default);

        // 3. 写入文件（补全 CRUD 闭环）
        Task SaveFileAsync(string filePath, Stream content, bool overwrite = true, CancellationToken cancellationToken = default);

        // 4. 目录检索
        Task<IEnumerable<string>> GetFileNamesAsync(string directoryPath, string searchPattern = "*.*", bool recursive = false, CancellationToken cancellationToken = default);

        // 5. 移动与删除
        Task MoveFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default);

        Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
