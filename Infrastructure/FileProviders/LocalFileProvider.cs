using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Common.Utilities;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    /// <summary>
    /// 本地文件提供程序：支持全盘绝对路径与当前工作目录相对路径的操作
    /// </summary>
    public class LocalFileProvider : IFileProvider, ICredentialSupported
    {
        private string _userName;
        private string _password;

        public LocalFileProvider()
        {
        }

        public void SetCredentials(string username, string password)
        {
            _userName = username;
            _password = password;
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
            using (SmbConnectionScope.ConnectIfNeeded(filePath, _userName, _password))
            {
                return File.Exists(Path.GetFullPath(filePath));
            }
        }

        public Task<Stream> GetFileStreamAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<Stream>(cancellationToken);

            try
            {
                // Path.GetFullPath 会处理相对路径并规范化（如处理 ../）
                var fullPath = Path.GetFullPath(filePath);

                var connection = SmbConnectionScope.ConnectIfNeeded(filePath, _userName, _password);

                // Allow readers to open files that are still being written by devices or Excel.
                var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
                return Task.FromResult<Stream>(new CredentialScopedStream(stream, connection));
            }
            catch (Exception ex)
            {
                if (IsSharingOrLockViolation(ex))
                {
                    return Task.FromException<Stream>(
                        new IOException(
                            ex.Message + " 该文件可能被独占锁定，当前读取模式已允许共享读写；请关闭占用程序，或后续启用复制采集/VSS方案。",
                            ex));
                }

                return Task.FromException<Stream>(ex);
            }
        }

        public Task<FileMetadata> GetFileMetadataAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(filePath);
                using (SmbConnectionScope.ConnectIfNeeded(filePath, _userName, _password))
                {
                    var info = new FileInfo(fullPath);
                    if (!info.Exists)
                    {
                        return new FileMetadata();
                    }

                    return new FileMetadata
                    {
                        LastWriteTime = info.LastWriteTime,
                        LastWriteTimeUtc = info.LastWriteTimeUtc,
                        Length = info.Length
                    };
                }
            }, cancellationToken);
        }

        public async Task SaveFileAsync(string filePath, Stream content, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            using (SmbConnectionScope.ConnectIfNeeded(filePath, _userName, _password))
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
        }

        public Task<IEnumerable<string>> GetFileNamesAsync(string directoryPath, string searchPattern = "*.*", bool recursive = false, CancellationToken cancellationToken = default)
        {
            // 对于这种 IO 密集型但没有原生异步 API 的操作，使用 Task.Run 包装
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(directoryPath);
                using (SmbConnectionScope.ConnectIfNeeded(directoryPath, _userName, _password))
                {
                    if (!Directory.Exists(fullPath)) return Enumerable.Empty<string>();

                    var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    // EnumerateFiles 比 GetFiles 内存占用更低，尤其在文件较多时
                    return (IEnumerable<string>)Directory.EnumerateFiles(fullPath, searchPattern, option).ToList();
                }
            }, cancellationToken);
        }

        public Task MoveFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var src = Path.GetFullPath(sourcePath);
                var dest = Path.GetFullPath(destPath);
                using (SmbConnectionScope.ConnectIfNeeded(sourcePath, _userName, _password))
                {

                    if (overwrite && File.Exists(dest))
                        File.Delete(dest);

                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Move(src, dest);
                }
            }, cancellationToken);
        }

        public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(filePath);
                using (SmbConnectionScope.ConnectIfNeeded(filePath, _userName, _password))
                {
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                }
            }, cancellationToken);
        }

        private static bool IsSharingOrLockViolation(Exception ex)
        {
            var ioException = ex as IOException;
            if (ioException == null) return false;

            int errorCode = ioException.HResult & 0xFFFF;
            return errorCode == 32 || errorCode == 33;
        }

        private sealed class CredentialScopedStream : Stream
        {
            private readonly Stream _inner;
            private readonly IDisposable _connection;

            public CredentialScopedStream(Stream inner, IDisposable connection)
            {
                _inner = inner;
                _connection = connection;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                    _connection?.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
