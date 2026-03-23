using Nancy;
using Nancy.ModelBinding;
using System;

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
            string val = (module.Request.Form[key] ?? module.Request.Query[key] ?? "false").ToString().ToLower();
            return val == "true" || val == "1";
        }

        // --- 响应工具 ---

        public static Response ToResponse(this NancyModule module, ResponseCode code, string info, object data = null)
        {
            return module.Response.AsJson(new { code = (int)code, info, data });
        }
    }
}