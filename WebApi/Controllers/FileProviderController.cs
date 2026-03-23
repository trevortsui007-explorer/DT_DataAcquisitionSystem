using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Common.Extensions;
using Nancy;
using Nancy.ModelBinding;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Learun.Application.WebApi;
using DT_DataAcquisitionSystem.Common.Utilities;
using DT_DataAcquisitionSystem.Application.Services;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    /// <summary>
    /// 文件操作接口：支持本地磁盘与 FTP
    /// </summary>
    public class FileProviderController : BaseApi
    {
        private readonly IFileProviderFactory _fileProviderFactory;

        public FileProviderController() : base("/api/files")
        {
            _fileProviderFactory = FileIocHelper.GetFileProviderFactory();

            // --- 路由映射 ---
            Get["/list", true] = async (p, ct) => await GetFileList(p, ct);
            Get["/exists", true] = async (p, ct) => await CheckFileExists(p, ct);
            Get["/download", true] = async (p, ct) => await DownloadFile(p, ct);
            Post["/upload", true] = async (p, ct) => await UploadFile(p, ct);
            Delete["/", true] = async (p, ct) => await DeleteFile(p, ct);
            Get["/preview", true] = async (p, ct) => await GetPreview(p, ct);
        }

        #region 业务接口实现

        private async Task<Response> GetFileList(dynamic p, CancellationToken ct)
        {
            string path = this.Request.Query["path"];
            string pattern = this.Request.Query["pattern"] ?? "*.*";
            string user = this.Request.Query["user"];
            string pass = this.Request.Query["pass"];

            if (string.IsNullOrEmpty(path))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "路径不能为空", null);

            try
            {
                var provider = _fileProviderFactory.Create(path, user, pass);
                var files = await provider.GetFileNamesAsync(path, pattern, false, ct);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", files);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"获取列表失败: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 获取解析路径文件数据
        /// </summary>
        private async Task<Response> GetPreview(dynamic p, CancellationToken ct)
        {
            // 获取参数
            string path = this.Request.Query["path"];
            int top = this.Request.Query["top"] ?? 10; // 默认 10 行

            if (string.IsNullOrEmpty(path))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "路径不能为空", null);

            try
            {
                // 实例化 Service (或者通过 DI 注入)
                var previewService = new DataPreviewService(_fileProviderFactory);

                var data = await previewService.GetFilePreview(path, top, ct);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    $"成功获取前 {data.Count} 行数据预览",
                    data
                );
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"预览失败: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 判断文件是否存在（已优化：同步 + try-catch + 路径校验）
        /// </summary>
        private async Task<object> CheckFileExists(dynamic p, CancellationToken ct)
        {
            string path = this.Request.Query["path"];
            string user = this.Request.Query["user"];
            string pass = this.Request.Query["pass"];

            if (string.IsNullOrEmpty(path))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "路径不能为空", null);

            try
            {
                var provider = _fileProviderFactory.Create(path, user, pass);
                bool exists = provider.Exists(path);

                // 直接返回 ToResponse 的结果，ToResponse 内部应该返回的是一个普通的类或匿名对象
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", new { FileExists = exists });
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"失败: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 下载文件（关键优化：文件流使用 using 自动释放）
        /// </summary>
        private async Task<Response> DownloadFile(dynamic p, CancellationToken ct)
        {
            string path = this.Request.Query["path"];
            string user = this.Request.Query["user"];
            string pass = this.Request.Query["pass"];

            if (string.IsNullOrEmpty(path))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "路径不能为空", null);

            try
            {
                var provider = _fileProviderFactory.Create(path, user, pass);
                if (!provider.Exists(path))
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "文件不存在", null);

                var stream = await provider.GetFileStreamAsync(path, ct);

                var filename = Path.GetFileName(path);
                var response = new Response
                {
                    ContentType = "application/octet-stream",
                    Contents = outputStream =>
                    {
                        using (stream)
                        {
                            stream.CopyTo(outputStream);
                        }
                    }
                };
                return response.AsAttachment(filename);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"文件下载失败: {ex.Message}", null);
            }
        }

        private async Task<Response> UploadFile(dynamic p, CancellationToken ct)
        {
            string targetPath = this.Request.Query["path"];
            string user = this.Request.Query["user"];
            string pass = this.Request.Query["pass"];

            if (string.IsNullOrEmpty(targetPath))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "目标路径不能为空", null);

            var file = this.Request.Files.FirstOrDefault();
            if (file == null)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "未检测到上传文件", null);

            try
            {
                var provider = _fileProviderFactory.Create(targetPath, user, pass);
                await provider.SaveFileAsync(targetPath, file.Value, true, ct);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "上传成功", null);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"上传失败: {ex.Message}", null);
            }
        }

        private async Task<Response> DeleteFile(dynamic p, CancellationToken ct)
        {
            string path = this.Request.Query["path"];
            string user = this.Request.Query["user"];
            string pass = this.Request.Query["pass"];

            if (string.IsNullOrEmpty(path))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "路径不能为空", null);

            try
            {
                var provider = _fileProviderFactory.Create(path, user, pass);
                await provider.DeleteFileAsync(path, ct);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "删除成功", null);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"删除失败: {ex.Message}", null);
            }
        }

        #endregion
    }
}