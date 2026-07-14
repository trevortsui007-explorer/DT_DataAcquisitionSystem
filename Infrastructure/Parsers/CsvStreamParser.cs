using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    public class CsvStreamParser : BaseStreamParser, IDataParser
    {
        public CsvStreamParser(Func<object, Type, object> converter) : base(converter) { }

        #region --- 同步方法 ---

        public List<T> Parse<T>(Stream stream, object options) where T : class, new()
        {
            // 对于 CSV，异步读取 StreamReader 是主流，这里使用 Task.Run 桥接同步接口
            return Task.Run(() => ParseAsync<T>(stream, options)).GetAwaiter().GetResult();
        }

        #endregion

        #region --- 异步方法 ---

        public async Task<List<T>> ParseAsync<T>(Stream stream, object options, CancellationToken ct = default) where T : class, new()
        {
            var opt = options as CsvParserOptions ?? new CsvParserOptions();
            var result = new List<T>();

            // 注意：leaveOpen 设为 true 保证外部 stream 不会被自动释放
            using (var reader = new StreamReader(stream, Encoding.Default, true, opt.BufferSize, leaveOpen: true))
            {
                string[] headers = null;
                int currentRow = 0;

                while (!reader.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    string line = await reader.ReadLineAsync();
                    currentRow++; // 物理行号增加

                    if (line == null) break;

                    // 1. 表头识别
                    if (currentRow == opt.HeaderRow)
                    {
                        headers = SplitLine(line, opt);
                        continue;
                    }

                    // 2. 范围过滤与空行过滤
                    if (currentRow < opt.StartRow) continue;
                    if (opt.SkipEmptyLines && string.IsNullOrWhiteSpace(line)) continue;

                    // 3. 核心映射 (调用基类方法)
                    if (headers != null)
                    {
                        var fields = SplitLine(line, opt);

                        // 直接使用基类的 MapToEntity，传入 headers, fields 以及提取好的元数据
                        result.Add(MapToEntity<T>(
                            headers,
                            fields,
                            currentRow,
                            opt.HasExtFields,
                            opt.FilePath,
                            opt.ExtFields,
                            fields));
                    }
                }
            }
            return result;
        }

        #endregion

        #region --- 内部工具 ---

        private string[] SplitLine(string line, CsvParserOptions opt)
        {
            // 以后如果要支持复杂的引号包含逗号（例如 "A,B",C），可以在这里更换为更复杂的正则或解析库
            var parts = line.Split(new[] { opt.Separator }, StringSplitOptions.None);

            return opt.TrimFields
                ? parts.Select(p => p.Trim()).ToArray()
                : parts;
        }

        #endregion
    }
}
