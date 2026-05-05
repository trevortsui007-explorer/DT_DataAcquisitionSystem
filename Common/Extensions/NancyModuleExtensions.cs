using Nancy;
using Nancy.ModelBinding;
using System;
using System.Collections.Generic;

namespace DT_DataAcquisitionSystem.Common.Extensions
{
    public static class NancyModuleExtensions
    {
        // 定义内部类解决兼容性问题
        public class DbContextInfo
        {
            public string TableName { get; set; }
            public string DatabaseName { get; set; }
        }

        public class PageResult<T>
        {
            public int Total { get; set; }
            public IEnumerable<T> List { get; set; }
        }

        public enum ResponseCode
        {
            success = 1,
            fail = 0,
            exception = -1
        }

        // --- 请求解析工具 ---

        public static string GetParam(this NancyModule module, string key, string defaultValue = null)
        {
            var val = module.Request.Query[key].HasValue ? module.Request.Query[key] : module.Request.Form[key];
            string strVal = (string)val;
            return string.IsNullOrWhiteSpace(strVal) ? defaultValue : strVal;
        }

        public static DbContextInfo GetQueryContext(this NancyModule module, string defaultTable = "DA_AcquisitionConfig", string defaultDb = "BaseDb")
        {
            return new DbContextInfo
            {
                TableName = module.GetParam("tableName", defaultTable),
                DatabaseName = module.GetParam("databaseName", defaultDb)
            };
        }

        public static string[] GetQueryArray(this NancyModule module, string key)
        {
            var val = module.Request.Query[key].HasValue ? module.Request.Query[key] : module.Request.Form[key];
            if (!val.HasValue) return null;

            string rawValue = (string)val;
            return string.IsNullOrEmpty(rawValue)
                ? null
                : rawValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static bool GetBool(this NancyModule module, string key)
        {
            var formValue = module.Request.Form[key];
            var queryValue = module.Request.Query[key];

            string val = null;

            if (formValue != null && formValue.HasValue)
            {
                val = formValue.Value.ToString();
            }
            else if (queryValue != null && queryValue.HasValue)
            {
                val = queryValue.Value.ToString();
            }

            val = (val ?? "false").Trim().ToLower();

            return val == "true" || val == "1";
        }

        // --- 响应工具 ---

        public static Response ToResponse(this NancyModule module, ResponseCode code, string info, object data = null)
        {
            return module.Response.AsJson(new { code = (int)code, info, data });
        }

        public static Response ToPageResponse(this NancyModule module, ResponseCode code, string info, int total, object data)
        {
            // 构造符合前端分页要求的匿名对象
            return module.Response.AsJson(new
            {
                code = (int)code,
                info = info,
                count = total, // 直接在这里返回 count 字段，方便前端直接读取
                data = data
            });
        }

        // 3. 辅助方法：获取分页参数
        public static int GetPage(this NancyModule module) => int.TryParse(module.GetParam("page"), out var p) ? p : 1;
        public static int GetLimit(this NancyModule module) => int.TryParse(module.GetParam("limit"), out var l) ? l : 10;
    }
}