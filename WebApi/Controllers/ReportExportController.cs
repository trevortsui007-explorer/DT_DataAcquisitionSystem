using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Common.Extensions;
using Learun.Application.WebApi;
using Nancy;
using Nancy.ModelBinding;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    public class ReportExportController : BaseApi
    {
        private readonly IReportExportService _reportExportService;

        public ReportExportController()
            : this(new ReportExportService())
        {
        }

        public ReportExportController(IReportExportService reportExportService)
            : base("/api/data-acquisition/export")
        {
            _reportExportService = reportExportService;

            Post["/tasks", true] = async (p, ct) => await CreateTask(p, ct);
            Get["/tasks/{id}", true] = async (p, ct) => await GetTask(p, ct);
            Get["/tasks/{id}/download", true] = async (p, ct) => await DownloadTaskFile(p, ct);
        }

        private async Task<Response> CreateTask(dynamic p, CancellationToken ct)
        {
            try
            {
                var request = this.Bind<ReportExportCreateRequestDto>();
                var result = await _reportExportService.CreateTaskAsync(request).ConfigureAwait(false);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "报表导出任务已创建", result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, ex.Message, null);
            }
        }

        private Task<Response> GetTask(dynamic p, CancellationToken ct)
        {
            try
            {
                string id = (string)p.id;
                var task = _reportExportService.GetTask(id);
                if (task == null)
                {
                    return Task.FromResult(this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "导出任务不存在", null));
                }
                return Task.FromResult(this.ToResponse(NancyModuleExtensions.ResponseCode.success, "获取导出任务成功", task));
            }
            catch (Exception ex)
            {
                return Task.FromResult(this.ToResponse(NancyModuleExtensions.ResponseCode.fail, ex.Message, null));
            }
        }

        private Task<Response> DownloadTaskFile(dynamic p, CancellationToken ct)
        {
            try
            {
                string id = (string)p.id;
                string fileName;
                string filePath = _reportExportService.GetDownloadFilePath(id, out fileName);
                var stream = File.OpenRead(filePath);

                var response = new Response
                {
                    ContentType = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        ? "application/zip"
                        : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    Contents = outputStream =>
                    {
                        using (stream)
                        {
                            stream.CopyTo(outputStream);
                        }
                    }
                };

                return Task.FromResult(response.AsAttachment(fileName));
            }
            catch (Exception ex)
            {
                return Task.FromResult(this.ToResponse(NancyModuleExtensions.ResponseCode.fail, ex.Message, null));
            }
        }
    }
}
