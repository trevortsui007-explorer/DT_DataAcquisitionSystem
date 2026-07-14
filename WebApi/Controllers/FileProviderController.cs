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
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Application.Services;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    /// <summary>
    /// 文件操作接口：支持本地磁盘与 FTP
    /// </summary>
    public class FileProviderController : BaseApi
    {
        private readonly IFileProviderFactory _fileProviderFactory;
        private readonly IFileConfigService _fileConfigService;
        private readonly IAcquisitionFileStateService _fileStateService;

        public FileProviderController(IFileConfigService fileConfigService) : base("/api/data-acquisition/files")
        {
            _fileProviderFactory = FileIocHelper.GetFileProviderFactory();
            _fileConfigService = fileConfigService;
            _fileStateService = new AcquisitionFileStateService();

            // --- 路由映射 ---
            Get["/list", true] = async (p, ct) => await GetFileList(p, ct);
            Get["/exists", true] = async (p, ct) => await CheckFileExists(p, ct);
            Get["/download", true] = async (p, ct) => await DownloadFile(p, ct);
            Post["/upload", true] = async (p, ct) => await UploadFile(p, ct);
            Delete["/", true] = async (p, ct) => await DeleteFile(p, ct);
            Get["/preview", true] = async (p, ct) => await GetPreview(p, ct);
            Get["/discovery", true] = async (p, ct) => await GetDetailedDiscovery(p, ct);
            Get["/group-discovery", true] = async (p, ct) => await GetGroupDiscovery(p, ct);
            Get["/state", true] = async (p, ct) => await GetFileState(p, ct);
        }

        #region 业务接口实现

        /// <summary>
        /// 获取详细的文件巡检清单（包含存在与缺失的文件）
        /// </summary>
        private async Task<Response> GetDetailedDiscovery(dynamic p, CancellationToken ct)
        {
            // 1. 获取基础参数
            string configId = this.Request.Query["configId"];
            string startStr = this.Request.Query["startTime"];
            string endStr = this.Request.Query["endTime"];
            string user = this.Request.Query["user"];
            string pass = this.Request.Query["pass"];

            // 2. 参数校验与时间转换
            if (string.IsNullOrEmpty(configId))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "配置ID不能为空", null);

            if (!DateTime.TryParse(startStr, out DateTime startDate))
                startDate = DateTime.Now.AddDays(-7); // 默认查最近一周

            if (!DateTime.TryParse(endStr, out DateTime endDate))
                endDate = DateTime.Now;

            try
            {
                // 3. 获取配置对象
                var config = _fileConfigService.GetByIds(new[] { configId }).FirstOrDefault();

                if (config == null)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "未找到对应的采集配置", null);

                // 4. 调用文件发现服务
                // 同样的，FileDiscoveryService 建议通过 DI 注入
                var discoveryService = new FileDiscoveryService();
                var list = await discoveryService.GetDetailedDiscoveryAsync(config, startDate, endDate, user, pass, ct);

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    "获取巡检清单成功",
                    list
                );
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"巡检失败: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 获取配置组下所有配置的单日巡检结果
        /// </summary>
        private async Task<Response> GetGroupDiscovery(dynamic p, CancellationToken ct)
        {
            string groupIdText = this.Request.Query["groupId"];
            string dateText = this.Request.Query["date"];
            string user = this.Request.Query["user"];
            string pass = this.Request.Query["pass"];

            if (!int.TryParse(groupIdText, out int groupId) || groupId <= 0)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "配置组ID不能为空", null);
            }

            if (!DateTime.TryParse(dateText, out DateTime targetDate))
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "巡检日期不能为空", null);
            }

            try
            {
                var configs = _fileConfigService
                    .GetConfigsByGroupIds(new[] { groupId.ToString() })
                    .ToList();

                var discoveryService = new FileDiscoveryService();
                var semaphore = new SemaphoreSlim(4);
                var tasks = configs.Select(async config =>
                {
                    await semaphore.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var discovery = await discoveryService
                            .GetDetailedDiscoveryAsync(config, targetDate.Date, targetDate.Date, user, pass, ct)
                            .ConfigureAwait(false);

                        bool isFolderListMode = discovery.Any(x =>
                            string.Equals(x.DiscoveryMode, "folder-list", StringComparison.OrdinalIgnoreCase));

                        var files = discovery
                            .SelectMany(x => x.Files ?? new List<FileEntryDto>())
                            .ToList();

                        var file = isFolderListMode
                            ? files.FirstOrDefault(x => !x.IsMissing)
                            : files.FirstOrDefault(x => x.DetectedDate.Date == targetDate.Date);

                        return new GroupFileDiscoveryItemDto
                        {
                            ConfigId = config.Id,
                            EqName = config.EqName,
                            IsMissing = isFolderListMode ? file == null : file?.IsMissing ?? true,
                            FullFilePath = file?.FullPath ?? string.Empty
                        };
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var result = new GroupFileDiscoveryDto
                {
                    GroupId = groupId,
                    Date = targetDate.Date,
                    Items = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList()
                };
                semaphore.Dispose();

                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    "获取配置组巡检结果成功",
                    result
                );
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"配置组巡检失败: {ex.Message}", null);
            }
        }

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

        private async Task<Response> GetFileState(dynamic p, CancellationToken ct)
        {
            if (!int.TryParse((string)this.Request.Query["configId"], out int configId) || configId <= 0)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "配置ID不能为空", null);
            }

            DateTime startDate;
            DateTime endDate;

            string businessDateText = this.Request.Query["businessDate"];
            if (DateTime.TryParse(businessDateText, out var businessDate))
            {
                startDate = businessDate.Date;
                endDate = businessDate.Date;
            }
            else
            {
                startDate = DateTime.TryParse((string)this.Request.Query["startTime"], out var start)
                    ? start.Date
                    : DateTime.Now.AddDays(-7).Date;

                endDate = DateTime.TryParse((string)this.Request.Query["endTime"], out var end)
                    ? end.Date
                    : DateTime.Now.Date;
            }

            if (endDate < startDate)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "结束日期不能早于开始日期", null);
            }

            var states = await _fileStateService
                .GetByConfigAndDateRangeAsync(configId, startDate, endDate, ct)
                .ConfigureAwait(false);

            string fileName = this.Request.Query["fileName"];
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                states = states
                    .Where(x => string.Equals(x.FileName, fileName.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "获取文件状态成功", states);
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
