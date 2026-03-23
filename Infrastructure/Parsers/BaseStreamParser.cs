using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.Linq;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    public abstract class BaseStreamParser
    {
        protected readonly Func<object, Type, object> _converter;

        // 反射缓存：避免频繁 GetProperties 损耗性能
        private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> _propertyCache
            = new ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>>();

        protected BaseStreamParser(Func<object, Type, object> converter)
        {
            _converter = converter;
        }

        /// <summary>
        /// 通用映射逻辑：支持 Dictionary 和 强类型实体
        /// </summary>
        protected T MapToEntity<T>(string[] headers, object[] values, int rowIndex, bool hasExt, string filePath) where T : class, new()
        {
            // 情况 A：处理 Dictionary<string, object>
            if (typeof(T) == typeof(Dictionary<string, object>))
            {
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                int minLen = Math.Min(headers.Length, values.Length);
                for (int i = 0; i < minLen; i++)
                {
                    dict[headers[i]] = values[i];
                }

                if (hasExt)
                {
                    dict["fullFilePath"] = filePath;
                    dict["row"] = rowIndex;
                }
                return dict as T;
            }

            // 情况 B：处理强类型实体
            T entity = new T();
            var propMap = GetPropertyMap<T>();
            int minLength = Math.Min(headers.Length, values.Length);

            for (int i = 0; i < minLength; i++)
            {
                if (propMap.TryGetValue(headers[i], out var prop))
                {
                    SetPropertyValue(entity, prop, values[i]);
                }
            }

            if (hasExt)
            {
                if (propMap.TryGetValue("fullFilePath", out var p1)) p1.SetValue(entity, filePath);
                if (propMap.TryGetValue("row", out var p2)) p2.SetValue(entity, rowIndex);
            }
            return entity;
        }

        private void SetPropertyValue(object entity, PropertyInfo prop, object rawValue)
        {
            object convertedValue = _converter(rawValue, prop.PropertyType);

            // 处理 Nullable 和 ValueType 的赋值安全
            if (convertedValue != null || !prop.PropertyType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) != null)
            {
                prop.SetValue(entity, convertedValue == DBNull.Value ? null : convertedValue);
            }
        }

        private Dictionary<string, PropertyInfo> GetPropertyMap<T>()
        {
            return _propertyCache.GetOrAdd(typeof(T), t =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Where(p => p.CanWrite)
                 .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase));
        }
    }
}