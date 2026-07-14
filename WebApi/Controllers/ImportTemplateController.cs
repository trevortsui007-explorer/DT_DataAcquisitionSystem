using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Common.Extensions;
using DT_DataAcquisitionSystem.Domain.Entities;
using Learun.Application.WebApi;
using Nancy;
using Nancy.ModelBinding;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    public class ImportTemplateController : BaseApi
    {
        private readonly IImportTemplateService _templateService;

        public ImportTemplateController(IImportTemplateService templateService) : base("/api/data-acquisition/import-templates")
        {
            _templateService = templateService;

            Get["/"] = GetTemplateList;
            Get["/{id:int}"] = GetTemplateEntity;
            Post["/"] = CreateTemplate;
            Put["/{id:int}"] = UpdateTemplate;
            Post["/preview", true] = async (p, ct) => await PreviewTemplate(p, ct);
        }

        private Response GetTemplateList(dynamic _)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_ExcelImportTemplate");
            var data = _templateService.GetList(ctx.TableName, ctx.DatabaseName);
            return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", data);
        }

        private Response GetTemplateEntity(dynamic p)
        {
            var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_ExcelImportTemplate");
            int id = (int)p.id;
            var entity = _templateService.GetById(id, ctx.TableName, ctx.DatabaseName);

            return entity != null
                ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "查询成功", entity)
                : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "未找到模板", null);
        }

        private Response CreateTemplate(dynamic _)
        {
            try
            {
                var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_ExcelImportTemplate");
                var template = this.Bind<ExcelImportTemplate>();

                if (template == null)
                {
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败", null);
                }

                int newId = _templateService.CreateTemplate(template, ctx.TableName, ctx.DatabaseName);
                return newId > 0
                    ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "模板创建成功", newId)
                    : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "模板创建失败", null);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"模板创建失败：{ex.Message}", null);
            }
        }

        private Response UpdateTemplate(dynamic p)
        {
            try
            {
                var ctx = NancyModuleExtensions.GetQueryContext(this, "DA_ExcelImportTemplate");
                var template = this.Bind<ExcelImportTemplate>();

                if (template == null)
                {
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "数据解析失败", null);
                }

                template.Id = (int)p.id;
                bool success = _templateService.UpdateTemplate(template, ctx.TableName, ctx.DatabaseName);

                return success
                    ? this.ToResponse(NancyModuleExtensions.ResponseCode.success, "模板更新成功", null)
                    : this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "模板更新失败", null);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"模板更新失败：{ex.Message}", null);
            }
        }

        private async Task<Response> PreviewTemplate(dynamic _, CancellationToken ct)
        {
            int templateId = ReadTemplateId();
            if (templateId <= 0)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "templateId 不能为空", null);
            }

            var file = this.Request.Files.FirstOrDefault();
            if (file == null)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "未检测到上传文件", null);
            }

            try
            {
                string fileName = string.IsNullOrWhiteSpace(file.Name) ? "preview.xlsx" : Path.GetFileName(file.Name);
                var rows = await _templateService.PreviewAsync(templateId, file.Value, fileName, ct)
                    .ConfigureAwait(false);

                var data = new
                {
                    TemplateId = templateId,
                    FileName = fileName,
                    TotalCount = rows.Count,
                    PreviewRows = rows.Take(5).ToList(),
                    Rows = rows
                };

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "预览成功", data);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"预览失败：{ex.Message}", null);
            }
        }

        private int ReadTemplateId()
        {
            string value = Convert.ToString(this.Request.Query["templateId"]);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = Convert.ToString(this.Request.Form["templateId"]);
            }

            return int.TryParse(value, out int templateId) ? templateId : 0;
        }
    }
}
