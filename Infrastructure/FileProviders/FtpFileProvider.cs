using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    public class FtpFileProvider : IFileProvider, ICredentialSupported
    {
        private string _user;
        private string _pass;

        public FtpFileProvider()
        {
        }

        public bool CanHandle(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);
        }

        public void SetCredentials(string user, string pass)
        {
            _user = user;
            _pass = pass;
        }

        public bool Exists(string filePath)
        {
            try
            {
                var request = CreateRequest(filePath, WebRequestMethods.Ftp.GetFileSize);
                using (var response = (FtpWebResponse)request.GetResponse())
                {
                    return response.StatusCode == FtpStatusCode.FileStatus;
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as FtpWebResponse;
                if (response != null && response.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                    return false;
                throw;
            }
        }

        public async Task<Stream> GetFileStreamAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var request = CreateRequest(filePath, WebRequestMethods.Ftp.DownloadFile);
            using (cancellationToken.Register(() => request.Abort()))
            {
                try
                {
                    var response = (FtpWebResponse)await request.GetResponseAsync();
                    return response.GetResponseStream();
                }
                catch (WebException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("FTP获取文件流操作已被取消", ex, cancellationToken);
                }
            }
        }

        public async Task SaveFileAsync(string filePath, Stream content, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            if (!overwrite && Exists(filePath))
                throw new IOException($"文件已存在: {filePath}");

            var request = CreateRequest(filePath, WebRequestMethods.Ftp.UploadFile);
            using (cancellationToken.Register(() => request.Abort()))
            {
                try
                {
                    using (var requestStream = await request.GetRequestStreamAsync())
                    {
                        await content.CopyToAsync(requestStream, 8192, cancellationToken);
                    }
                }
                catch (WebException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("FTP上传操作已被取消", ex, cancellationToken);
                }
            }
        }

        public async Task<IEnumerable<string>> GetFileNamesAsync(string directoryPath, string searchPattern = "*.*", bool recursive = false, CancellationToken cancellationToken = default)
        {
            string normalizedPath = directoryPath.EndsWith("/") ? directoryPath : directoryPath + "/";
            var request = CreateRequest(normalizedPath, WebRequestMethods.Ftp.ListDirectory);
            var result = new List<string>();

            using (cancellationToken.Register(() => request.Abort()))
            {
                try
                {
                    using (var response = (FtpWebResponse)await request.GetResponseAsync())
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            string fileName = Path.GetFileName(line);
                            if (string.IsNullOrEmpty(fileName)) continue;

                            if (IsMatch(fileName, searchPattern))
                            {
                                result.Add(fileName);
                            }
                        }
                    }
                }
                catch (WebException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("FTP获取列表操作已被取消", ex, cancellationToken);
                }
                catch (WebException ex)
                {
                    var resp = ex.Response as FtpWebResponse;
                    if (resp?.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                        return new List<string>();
                    throw;
                }
            }
            return result;
        }

        public async Task MoveFileAsync(string sourcePath, string destPath, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            if (overwrite && Exists(destPath))
                await DeleteFileAsync(destPath, cancellationToken);

            var request = CreateRequest(sourcePath, WebRequestMethods.Ftp.Rename);
            request.RenameTo = destPath;

            using (cancellationToken.Register(() => request.Abort()))
            {
                try
                {
                    using (await request.GetResponseAsync()) { }
                }
                catch (WebException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("FTP移动操作已被取消", ex, cancellationToken);
                }
            }
        }

        public async Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var request = CreateRequest(filePath, WebRequestMethods.Ftp.DeleteFile);
            using (cancellationToken.Register(() => request.Abort()))
            {
                try
                {
                    using (await request.GetResponseAsync()) { }
                }
                catch (WebException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("FTP删除操作已被取消", ex, cancellationToken);
                }
            }
        }

        #region 内部辅助方法
        private FtpWebRequest CreateRequest(string path, string method)
        {
            var uri = path.Replace("\\", "/");
            var request = (FtpWebRequest)WebRequest.Create(uri);
            request.Method = method;
            if (!string.IsNullOrEmpty(_user))
            {
                request.Credentials = new NetworkCredential(_user, _pass);
            }
            request.UseBinary = true;
            request.KeepAlive = false;
            return request;
        }

        private bool IsMatch(string fileName, string searchPattern)
        {
            if (string.IsNullOrEmpty(searchPattern) || searchPattern == "*.*" || searchPattern == "*")
                return true;
            var extension = searchPattern.Replace("*", "");
            return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }
        #endregion
    }
}