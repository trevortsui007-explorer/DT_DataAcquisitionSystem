using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Common.Extensions;
using Nancy;
using Nancy.ModelBinding;
using System;
using System.Linq;
using System.Threading.Tasks;
using Learun.Application.WebApi;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Application.DTOs;
using System.Data;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    /// <summary>
    /// 数据库表管理接口（自动建表等）
    /// </summary>
    public class TableManagementController : BaseApi
    {
        private readonly IDataService _dataService;

        public TableManagementController(IDataService dataService) : base("/api/data-acquisition")
        {
            _dataService = dataService;

            // 1. 自动建表接口 (POST)
            Post["/create-table", true] = async (p, ct) => await CreateTable(p, ct);

            // 2. 获取表结构（GET）
            Get["/fields/{tableName}", true] = async (p, ct) => await GetTableFields(p, ct);
        }

        #region 接口实现

        private async Task<dynamic> CreateTable(dynamic p, System.Threading.CancellationToken ct)
        {
            // 1. 绑定并校验请求数据
            var request = this.Bind<CreateTableRequest>();

            if (request == null || string.IsNullOrWhiteSpace(request.TableName))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "表名不能为空", null);

            if (request.Columns == null || !request.Columns.Any())
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "列定义不能为空", null);

            try
            {
                // 2. 将 DTO 映射为 Domain 层的 ColumnDefinition
                var domainColumns = request.Columns.Select(c => new ColumnDefinition
                {
                    ColumnName = c.ColumnName,
                    DataType = MapStringToType(c.DataType),
                    IsPrimaryKey = c.IsPrimaryKey,
                    AllowNull = c.AllowNull,
                    MaxLength = c.MaxLength
                }).ToList();

                // 3. 调用底层服务建表
                await _dataService.CreateTableIfNotExistsAsync(request.TableName, domainColumns, ct);

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, $"表 [{request.TableName}] 验证/创建成功", new
                {
                    TableName = request.TableName,
                    ColumnCount = domainColumns.Count
                });
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"建表发生异常: {ex.Message}", null);
            }
        }

        private async Task<dynamic> GetTableFields(dynamic p, System.Threading.CancellationToken ct)
        {
            string tableName = p.tableName;

            if (string.IsNullOrWhiteSpace(tableName))
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "表名不能为空", null);

            try
            {
                // 1. 调用 SqlDataService 获取 Schema
                DataTable schema = await _dataService.GetTableSchemaAsync(tableName);

                if (schema == null)
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"未找到表: {tableName}", null);

                // 2. 将 DataTable 的 Columns 转换为 DTO 列表
                var columns = new List<TableColumnInfo>();
                foreach (DataColumn col in schema.Columns)
                {
                    columns.Add(new TableColumnInfo
                    {
                        ColumnName = col.ColumnName,
                        // 返回更易读的 .NET 类型名称
                        DataType = col.DataType.Name,
                        AllowDBNull = col.AllowDBNull,
                        IsIdentity = col.AutoIncrement,
                        // 通过比较 schema 的主键集合来判断
                        IsPrimaryKey = schema.PrimaryKey.Any(pk => pk.ColumnName == col.ColumnName),
                        MaxLength = col.MaxLength,
                        DefaultValue = col.DefaultValue?.ToString()
                    });
                }

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "获取成功", columns);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, $"获取表字段失败: {ex.Message}", null);
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 将前端传入的字符串类型转换为 C# 真实的 Type
        /// </summary>
        private Type MapStringToType(string typeStr)
        {
            if (string.IsNullOrWhiteSpace(typeStr)) return typeof(string);

            switch (typeStr.Trim().ToLower())
            {
                case "int":
                case "integer":
                    return typeof(int);
                case "long":
                case "bigint":
                    return typeof(long);
                case "decimal":
                case "double":
                case "float":
                case "number":
                    return typeof(decimal);
                case "datetime":
                case "date":
                    return typeof(DateTime);
                case "bool":
                case "boolean":
                case "bit":
                    return typeof(bool);
                case "guid":
                case "uuid":
                    return typeof(Guid);
                case "string":
                case "text":
                case "varchar":
                case "nvarchar":
                default:
                    return typeof(string);
            }
        }

        #endregion
    }
}