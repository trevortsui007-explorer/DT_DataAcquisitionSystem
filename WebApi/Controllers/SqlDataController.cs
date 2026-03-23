using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Common.Extensions;
using Nancy;
using Nancy.ModelBinding;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Learun.Application.WebApi;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    /// <summary>
    /// 通用高性能数据采集与导入接口
    /// </summary>
    public class FileConfigDataApi : BaseApi
    {
        private readonly IDataService _dataService;

        public FileConfigDataApi(IDataService dataService) : base("/api/data-acquisition")
        {
            _dataService = dataService;

            // --- 批量数据导入路由映射 ---

            // 1. 高性能批量导入接口 (POST)
            Post["/bulk-import", true] = async (p, ct) => await BulkImport(p, ct);

            // 2. 执行后置处理存储过程 (POST)
            Post["/execute-post-process", true] = async (p, ct) => await ExecutePostProcess(p, ct);
        }

        #region 接口实现

        /// <summary>
        /// 批量导入请求模型
        /// </summary>
        public class BulkImportRequest
        {
            public string TableName { get; set; }
            public IEnumerable<IDictionary<string, object>> Data { get; set; }
            public string Flag { get; set; }
            public string PostProcessSproc { get; set; }
        }

        /// <summary>
        /// 执行高性能批量导入
        /// </summary>
        private async Task<dynamic> BulkImport(dynamic p, System.Threading.CancellationToken ct)
        {
            // 1. 绑定并校验请求数据
            var request = this.Bind<BulkImportRequest>();

            if (request == null || string.IsNullOrEmpty(request.TableName))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "请求参数错误，TableName 不能为空", null);

            if (request.Data == null)
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "待导入数据 Data 不能为空", null);

            try
            {
                // 2. 获取目标表的元数据 (Schema)
                // 内部已优化：包含 KeyInfo，能识别 Identity 自增列
                DataTable schema = await _dataService.GetTableSchemaAsync(request.TableName);

                if (schema == null)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"无法获取表结构: {request.TableName}", null);

                // 3. 填充并转换 DataTable
                // 内部已优化：类型转换防崩溃、智能过滤自增列、GUID 安全解析
                DataTable dataToInsert = _dataService.PopulateDataTable(request.Data, schema);

                // 4. 执行异步批量写入 (SqlBulkCopy)
                await _dataService.BulkInsertAsync(dataToInsert, request.TableName, ct);

                // 5. 可选：如果请求中指定了后置处理存储过程，则执行
                if (!string.IsNullOrEmpty(request.PostProcessSproc))
                {
                    await _dataService.ExecuteStoredProcedureAsync(request.Flag, request.PostProcessSproc, ct);
                }

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "批量导入成功", new
                {
                    rowCount = dataToInsert.Rows.Count,
                    tableName = request.TableName
                });
            }
            catch (Exception ex)
            {
                // 建议在 BaseApi 或 Middleware 中统一捕获，此处作为业务异常处理
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"导入发生致命错误: {ex.Message}", null);
            }
        }

        /// <summary>
        /// 独立执行后置存储过程
        /// </summary>
        private async Task<dynamic> ExecutePostProcess(dynamic p, System.Threading.CancellationToken ct)
        {
            var flag = this.GetParam("flag");
            var sproc = this.GetParam("sproc");

            if (string.IsNullOrEmpty(sproc))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "缺少参数 sproc", null);

            try
            {
                await _dataService.ExecuteStoredProcedureAsync(flag, sproc, ct);
                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "存储过程执行成功", null);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"存储过程执行失败: {ex.Message}", null);
            }
        }

        #endregion
    }
}