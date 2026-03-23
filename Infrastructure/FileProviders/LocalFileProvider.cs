using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    /// <summary>
    /// 本地文件提供程序：支持全盘绝对路径与当前工作目录相对路径的操作
    /// </summary>
    public class LocalFileProvider : IFileProvider
    {
        public LocalFileProvider()
        {
        }

        public bool CanHandle(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            // 如果不包含协议头（如 http://, ftp://）或者是绝对路径，则由本地处理器处理
            return !path.Contains("://") || Path.IsPathRooted(path);
        }

        public bool Exists(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            return File.Exists(Path.GetFullPath(filePath));
        }

        public Task<Stream> GetFileStreamAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<Stream>(cancellationToken);

            try
            {
                // Path.GetFullPath 会处理相对路径并规范化（如处理 ../）
                var fullPath = Path.GetFullPath(filePath);

                // 使用异步标志打开文件流
                var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                return Task.FromResult<Stream>(stream);
            }
            catch (Exception ex)
            {
                return Task.FromException<Stream>(ex);
            }
        }

        public async Task SaveFileAsync(string filePath, Stream content, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);

            // 自动创建不存在的父级目录
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;

            using (var fileStream = new FileStream(fullPath, mode, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await content.CopyToAsync(fileStream, 8192, cancellationToken);
            }
        }

        public Task<IEnumerable<string>> GetFileNamesAsync(string directoryPath, string searchPattern = "*.*", bool recursive = false, CancellationToken cancellationToken = default)
        {
            // 对于这种 IO 密集型但没有原生异步 API 的操作，使用 Task.Run 包装
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(directoryPath);
                if (!Directory.Exists(fullPath)) return Enumerable.Empty<string>();

                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                // EnumerateFiles 比 GetFiles 内存占用更低，尤其在文件较多时
                return (IEnumerable<string>)Directory.EnumerateFiles(fullPath, searchPattern, option).ToList();
            }, cancellationToken);
        }

        public Task MoveFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var src = Path.GetFullPath(sourcePath);
                var dest = Path.GetFullPath(destPath);

                if (overwrite && File.Exists(dest))
                    File.Delete(dest);

                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                File.Move(src, dest);
            }, cancellationToken);
        }

        public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(filePath);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }, cancellationToken);
        }
    }
}