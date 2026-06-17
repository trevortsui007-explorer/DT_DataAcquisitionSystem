using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Common;
using Nancy;
using DT_DataAcquisitionSystem.Common.Utilities;
using DT_DataAcquisitionSystem.Application.DTOs;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class DataAcquisitionService : IDataAcquisitionService
    {
        private readonly IFileProviderFactory _fileFactory;
        private readonly IFileConfigService _configService;
        private readonly IDataService _dataService;
        private readonly IAcquisitionLogService _logService;
        private readonly IAcquisitionFileStateService _fileStateService;
        private readonly IPostProcessingService _postProcessingService;

        // 并发控制：防止同时处理过多任务撑爆数据库连接池
        private readonly SemaphoreSlim _concurrencySemaphore = new SemaphoreSlim(5);
        // 内存级文件指纹去重（防止同一周期内重复读取）
        private readonly ConcurrentHashSet<string> _processingFiles = new ConcurrentHashSet<string>();

        // FTP 服务器凭证
        private string _username = "et1";
        private string _password = "dt123456#";

        public DataAcquisitionService(
            IFileConfigService configService,
            IDataService dataService,
            IAcquisitionLogService logService
            )
        {
            _fileFactory = FileIocHelper.GetFileProviderFactory();
            _configService = configService;
            _dataService = dataService;
            _logService = logService;
            _fileStateService = new AcquisitionFileStateService();
            _postProcessingService = ProcessorIocHelper.CreatePostProcessingService(dataService);
        }

        #region 批量处理逻辑

        /// <summary>
        /// 按任务 ID 批量处理
        /// </summary>
        public async Task<AcquisitionSummary> ProcessByTask(string[] taskids, DateTime processdate, CancellationToken ct = default)
        {
            var configs = _configService.GetConfigsByTaskIds(taskids);
            return await ExecuteBatchInternal(configs, processdate, processdate, ct, ResolveManualUpdateSource(processdate, processdate));
        }

        /// <summary>
        /// 按组 ID 批量处理
        /// </summary>
        public async Task<AcquisitionSummary> ProcessByGroup(string[] groupIds, DateTime processDate, CancellationToken ct = default)
        {
            var configs = _configService.GetConfigsByGroupIds(groupIds);
            return await ExecuteBatchInternal(configs, processDate, processDate, ct, ResolveManualUpdateSource(processDate, processDate));
        }

        /// <summary>
        /// 按配置 ID 数组批量处理
        /// </summary>
        public async Task<AcquisitionSummary> ProcessByIds(string[] ids, DateTime processDate, CancellationToken ct = default)
        {
            var configs = _configService.GetByIds(ids);
            return await ExecuteBatchInternal(configs, processDate, processDate, ct, ResolveManualUpdateSource(processDate, processDate));
        }

        /// <summary>
        /// 按时间范围补录单个配置的数据
        /// </summary>
        public async Task<AcquisitionSummary> ProcessByTimeRange(AcquisitionConfig config, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            return await ExecuteBatchInternal(new[] { config }, startDate, endDate, ct, ResolveManualUpdateSource(startDate, endDate));
        }        
        
        /// <summary>
        /// 按时间范围补录多个配置的数据
        /// </summary>
        public async Task<AcquisitionSummary> ProcessConfigsByTimeRange(FileConfigQueryOptions options, DateTime startDate, DateTime endDate, CancellationToken ct = default)
        {
            // 获取配置
            var configs = _configService.GetFileConfigs(options);

            if (configs == null || !configs.Any())
            {
                throw new Exception("未找到匹配查询条件的任何采集配置");
            }

            return await ExecuteBatchInternal(configs, startDate, endDate, ct, ResolveManualUpdateSource(startDate, endDate));
        }

        /// <summary>
        /// 核心：任务打平并行执行器
        /// </summary>
        private async Task<AcquisitionSummary> ExecuteBatchInternal(IEnumerable<AcquisitionConfig> configs, DateTime start, DateTime end, CancellationToken ct, string updateSource)
        {
            var configList = (configs ?? Enumerable.Empty<AcquisitionConfig>()).ToList();

            // 采集开始：记录手动Task日志
            var taskLogEntry = new AcquisitionTaskLogEntry
            {
                TaskId = 0,
                StartTime = DateTime.Now,
                Status = "Running",
                TotalConfigs = configList.Count * ((end.Date - start.Date).Days + 1),
                SuccessCount = 0,
                FailureCount = 0,
                ProcessedCount = 0,
                Progress = 0,
                Message = "任务已创建，等待执行。"
            };
            string taskLogId = await _logService.RecordTaskLogEntryAsync(taskLogEntry, ct);

            return await ExecuteBatchWithTaskLogAsync(configList, start, end, taskLogId, ct, updateSource).ConfigureAwait(false);
        }

        #endregion

        #region 单文件处理与明细日志记录

        /// <summary>
        /// 处理单个配置
        /// </summary>
        public async Task<Response> ProcessSingleConfig(AcquisitionConfig config, DateTime processDate, CancellationToken ct = default)
        {
            // 1. 获取文件路径并创建 FileProvider
            string path = FileDateTimeUtil.GetProcessedFilePath(config, processDate);
            var provider = _fileFactory.Create(path, _username, _password);

            if (!provider.Exists(path))
            {
                throw new FileNotFoundException($"未找到指定文件: {path}", path);
            }

            IEnumerable<Dictionary<string, object>> fullData;

            // 2. 获取并解析文件流
            using (var stream = await provider.GetFileStreamAsync(path, ct))
            {
                string ext = Path.GetExtension(path);
                var parser = DataParserIocHelper.GetParser(ext);

                // 构造配置：开启额外字段采集
                var parserPreviewOptions = DataParserIocHelper.CreateOptions(ext, path, hasExtFields: true);

                fullData = await parser.ParseAsync<Dictionary<string, object>>(stream, parserPreviewOptions, ct);
            }

            // 3. 预处理：使数据字段和数据库表字段对应
            var processedData = fullData.Select(row => DataMapperUtil.MapRow(row, config.FieldMappings)).ToList();

            try
            {
                // 4. 获取目标表的元数据 (Schema)
                DataTable schema = await _dataService.GetTableSchemaAsync(config.TableName);
                if (schema == null)
                {
                    // 修复：不在 catch 块中不能直接使用 throw;，需要抛出具体异常
                    throw new InvalidOperationException($"无法获取表架构: {config.TableName}");
                }

                // 5. 填充并转换 DataTable
                DataTable dataToInsert = _dataService.PopulateDataTable(processedData, schema);

                // 6. 执行异步批量写入 (SqlBulkCopy)
                await _dataService.BulkInsertAsync(dataToInsert, config.TableName, ct);

                // 7. 执行可选的后置处理存储过程
                if (!string.IsNullOrEmpty(config.ProcedureName))
                {
                    await _dataService.ExecuteStoredProcedureAsync(config.Flag, config.ProcedureName, ct);
                }

                // 8. 返回成功响应
                return null;
            }
            catch (Exception ex)
            {
                // 修复：捕获异常后记录日志，而不是吞掉异常
                // _logService.LogError($"处理文件 {path} 失败", ex); 

                throw new Exception($"数据采集处理失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 处理单个配置 (支持单文件模式 和 文件夹批量模式)
        /// </summary>
        public async Task ProcessSingleConfig(AcquisitionConfig config, DateTime processDate, string taskLogId, CancellationToken ct = default, string updateSource = null)
        {
            updateSource = string.IsNullOrWhiteSpace(updateSource)
                ? ResolveManualUpdateSource(processDate, processDate)
                : updateSource;

            // 并发锁：防止重复启动同一任务
            string fileKey = $"{config.Id}_{processDate:yyyyMMdd}";
            if (!_processingFiles.Add(fileKey)) return;

            try
            {
                // 1. 解析路径和文件名
                string path = FileDateTimeUtil.GetProcessedFilePath(config, processDate);   // 如果fileName为空，会返回folderPath；非空返回完整路径

                string filename = FileDateTimeUtil.GetProcessedFileName(config, processDate);

                var provider = _fileFactory.Create(path, _username, _password);
                List<string> targetFiles = new List<string>();

                // 2. 模式判断：单文件还是文件夹
                if (!string.IsNullOrEmpty(filename))
                {
                    // 【单文件模式】
                    targetFiles.Add(path);
                }
                else
                {
                    // 【文件夹模式】
                    // 默认取配置的扩展名，例如 ".csv" -> "*.csv"；如果没有则取 "*.*"
                    string pattern = string.IsNullOrEmpty(config.FileType) ? "*.*" : $"*{config.FileType}";

                    // 调用你写好的底层方法获取文件列表
                    var files = await provider.GetFileNamesAsync(path, pattern, false, ct);
                    if (files != null && files.Any())
                    {
                        targetFiles.AddRange(files);
                    }
                }

                // 判断文件数量
                if (targetFiles.Count == 0)
                {
                    throw new FileNotFoundException($"未找到可处理文件: {path}");
                }

                int failedFileCount = 0;
                List<string> errorMessages = new List<string>();

                // 3. 遍历处理所有目标文件
                foreach (var filePath in targetFiles)
                {
                    try
                    {
                        // 核心：调用独立的单文件执行器
                        await ProcessFileInternal(config, processDate, filePath, taskLogId, provider, ct, updateSource);
                    }
                    catch (Exception ex)
                    {
                        // 文件夹模式下：单个文件报错不中断整个文件夹的采集
                        // 错误已经在 ProcessFileInternal 中记录到了明细日志表
                        failedFileCount++;
                        errorMessages.Add($"文件 {Path.GetFileName(filePath)} 处理失败: {ex.Message}");
                        Console.WriteLine($"[警告] 文件处理失败跳过: {filePath}, 原因: {ex.Message}");
                    }
                }

                if (failedFileCount > 0)
                {
                    throw new Exception($"配置 {config.EqName} 在 {processDate:yyyy-MM-dd} 处理失败，失败文件数：{failedFileCount}。{string.Join("；", errorMessages)}");   
                }
            }
            finally
            {
                // 结束释放锁
                _processingFiles.Remove(fileKey);
            }
        }

        /// <summary>
        /// 核心执行器：处理具体的物理文件（包含断点续传、解析、入库、写日志）
        /// </summary>
        private async Task ProcessFileInternal(AcquisitionConfig config, DateTime businessDate, string filePath, string taskLogId, IFileProvider provider, CancellationToken ct, string updateSource)
        {
            string actualFileName = Path.GetFileName(filePath);
            int startRow = 0;
            int processedRows = 0;
            Exception postProcessingException = null;

            // 1. 初始化明细日志（每个物理文件一条记录）
            var logEntry = new AcquisitionLogEntry
            {
                TaskLogId = taskLogId,
                ConfigId = config.Id,
                FileName = actualFileName,
                StartTime = DateTime.Now,
                Status = "Running"
            };

            try
            {
                // 2. 防重与断点续传检查
                if (await _fileStateService.ShouldSkipForSealedAsync(config.Id, businessDate, actualFileName, updateSource, ct).ConfigureAwait(false))
                {
                    return;
                }

                startRow = await _logService.GetNextStartRowAsync(config.Id, actualFileName, ct);
                logEntry.StartRow = startRow;

                if (!provider.Exists(filePath))
                {
                    throw new FileNotFoundException($"文件未找到: {filePath}");
                }

                // 3. 获取并解析文件流
                IEnumerable<Dictionary<string, object>> fullData;
                using (var stream = await provider.GetFileStreamAsync(filePath, ct))
                {
                    string ext = Path.GetExtension(filePath);
                    var parser = DataParserIocHelper.GetParser(ext);
                    ParserOptionsBase options = DataParserIocHelper.CreateOptions(ext, filePath, hasExtFields: true);
                    options.StartRow = startRow; // 通知解析器跳过已读行

                    fullData = await parser.ParseAsync<Dictionary<string, object>>(stream, options, ct);
                }

                // 4. 数据预处理
                var processedData = fullData.Select(row => DataMapperUtil.MapRow(row, config.FieldMappings)).ToList();
                processedRows = processedData.Count;

                // 5. 入库
                if (processedRows > 0)
                {
                    DataTable schema = await _dataService.GetTableSchemaAsync(config.TableName);
                    if (schema == null) throw new InvalidOperationException($"表 {config.TableName} 架构不存在");

                    DataTable dataToInsert = _dataService.PopulateDataTable(processedData, schema);
                    var postProcessingRows = ExtractPostProcessingRowKeys(dataToInsert);
                    await _dataService.BulkInsertAsync(dataToInsert, config.TableName, ct);

                    // 6. 执行数据后处理 0 - 不处理；1 - 使用存储过程处理； 2- 使用C# Service处理
                    try
                    {
                        await _postProcessingService.ProcessAsync(new PostProcessingContext
                        {
                            Config = config,
                            TaskLogId = taskLogId,
                            BusinessDate = businessDate.Date,
                            SourceTableName = config.TableName,
                            PostTableName = config.PostTableName,
                            FileName = actualFileName,
                            FullPath = filePath,
                            Rows = postProcessingRows
                        }, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        postProcessingException = ex;
                    }
                }

                // 7. 成功：完善并写入明细日志
                logEntry.ProcessedRows = processedRows;
                logEntry.EndTime = DateTime.Now;
                logEntry.Status = "Success";
                await _logService.RecordLogEntryAsync(logEntry, ct);
                await _fileStateService.UpsertSuccessAsync(config, businessDate, filePath, logEntry, updateSource, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 失败：记录错误到明细日志
                logEntry.EndTime = DateTime.Now;
                logEntry.Status = "Failed";
                logEntry.ErrorMessage = ex.Message.Length > 1000 ? ex.Message.Substring(0, 1000) : ex.Message;
                logEntry.ProcessedRows = processedRows;

                await _logService.RecordLogEntryAsync(logEntry, ct);

                // 继续向上抛出，让外层的 foreach 捕获（如果是文件夹模式，会被外层吃掉异常继续下一个；如果是单文件，可按需处理）
                throw new Exception($"处理文件 {filePath} 失败: {ex.Message}", ex);
            }

            if (postProcessingException != null)
            {
                throw new Exception($"Post processing failed: {postProcessingException.Message}", postProcessingException);
            }
        }

        #endregion

        #region 批量处理结合日志系统
        public async Task<AcquisitionSummary> ExecuteBatchWithTaskLogAsync(IEnumerable<AcquisitionConfig> configs, DateTime start, DateTime end, string taskLogId, CancellationToken ct = default, string updateSource = null, bool sealOnSuccess = false)
        {
            if (string.IsNullOrWhiteSpace(taskLogId))
                throw new ArgumentNullException(nameof(taskLogId));

            updateSource = string.IsNullOrWhiteSpace(updateSource)
                ? ResolveManualUpdateSource(start, end)
                : updateSource;

            var configList = (configs ?? Enumerable.Empty<AcquisitionConfig>()).ToList();
            var summary = new AcquisitionSummary();

            int totalCount = configList.Count * ((end.Date - start.Date).Days + 1);

            if (totalCount <= 0)
            {
                await _logService.CompleteTaskAsync(
                    taskLogId,
                    "NoData",
                    0,
                    0,
                    0,
                    "没有可执行的采集配置。",
                    ct
                ).ConfigureAwait(false);

                return summary;
            }

            var tasks = new List<Task>();

            foreach (var config in configList)
            {
                for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
                {
                    var targetDate = date;

                    var task = Task.Run(async () =>
                    {
                        await _concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            await ProcessSingleConfig(config, targetDate, taskLogId, ct, updateSource).ConfigureAwait(false);
                            Interlocked.Increment(ref summary.SuccessCount);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref summary.FailureCount);
                            lock (summary.ErrorDetails)
                            {
                                summary.ErrorDetails.Add(
                                    $"[配置:{config.EqName}][日期:{targetDate:yyyy-MM-dd}] 失败: {ex.Message}");
                            }
                        }
                        finally
                        {
                            int processedCount = summary.SuccessCount + summary.FailureCount;

                            await _logService.UpdateTaskProgressAsync(
                                taskLogId,
                                "Running",
                                totalCount,
                                summary.SuccessCount,
                                summary.FailureCount,
                                $"运行中：{processedCount}/{totalCount}",
                                ct
                            ).ConfigureAwait(false);

                            _concurrencySemaphore.Release();
                        }
                    }, ct);

                    tasks.Add(task);
                }
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                // 子任务异常已在各自内部累计失败数并记录，这里不重复抛出
            }

            string finalStatus;
            if (summary.SuccessCount > 0 && summary.FailureCount == 0)
                finalStatus = "Success";
            else if (summary.SuccessCount == 0 && summary.FailureCount > 0)
                finalStatus = "Failed";
            else if (summary.SuccessCount > 0 && summary.FailureCount > 0)
                finalStatus = "PartialSuccess";
            else
                finalStatus = "NoData";

            string finalMessage;

            if (finalStatus == "Success")
            {
                finalMessage = "任务完成";
            }
            else if (finalStatus == "Failed")
            {
                finalMessage = $"任务完成，但全部失败。失败数：{summary.FailureCount}";
            }
            else if (finalStatus == "PartialSuccess")
            {
                finalMessage = $"任务完成，部分失败。成功数：{summary.SuccessCount}，失败数：{summary.FailureCount}";
            }
            else
            {
                finalMessage = "没有可执行的数据。";
            }

            await _logService.CompleteTaskAsync(
                taskLogId,
                finalStatus,
                totalCount,
                summary.SuccessCount,
                summary.FailureCount,
                finalMessage,
                ct
            ).ConfigureAwait(false);

            if (sealOnSuccess && finalStatus == "Success")
            {
                await _fileStateService.SealByTaskLogAsync(taskLogId, ct).ConfigureAwait(false);
            }

            return summary;
        }

        private static string ResolveManualUpdateSource(DateTime startDate, DateTime endDate)
        {
            return endDate.Date < DateTime.Today
                ? FileStateUpdateSources.ManualRepair
                : FileStateUpdateSources.ManualCurrent;
        }
        private static List<PostProcessingRowKey> ExtractPostProcessingRowKeys(DataTable dataTable)
        {
            var result = new List<PostProcessingRowKey>();
            if (dataTable == null || dataTable.Rows.Count == 0) return result;

            DataColumn idColumn = FindColumn(dataTable, "Id");
            if (idColumn == null) return result;

            DataColumn fullPathColumn = FindColumn(dataTable, "fullFilePath");
            DataColumn rowColumn = FindColumn(dataTable, "row");

            foreach (DataRow dataRow in dataTable.Rows)
            {
                if (dataRow.IsNull(idColumn)) continue;

                Guid id;
                object idValue = dataRow[idColumn];
                if (idValue is Guid)
                {
                    id = (Guid)idValue;
                }
                else if (!Guid.TryParse(Convert.ToString(idValue), out id))
                {
                    continue;
                }

                if (id == Guid.Empty) continue;

                int rowNumber;
                int? sourceRow = null;
                if (rowColumn != null && !dataRow.IsNull(rowColumn) && int.TryParse(Convert.ToString(dataRow[rowColumn]), out rowNumber))
                {
                    sourceRow = rowNumber;
                }

                result.Add(new PostProcessingRowKey
                {
                    Id = id,
                    FullPath = fullPathColumn == null || dataRow.IsNull(fullPathColumn)
                        ? null
                        : Convert.ToString(dataRow[fullPathColumn]),
                    Row = sourceRow
                });
            }

            return result;
        }

        private static DataColumn FindColumn(DataTable dataTable, string columnName)
        {
            if (dataTable == null || string.IsNullOrWhiteSpace(columnName)) return null;

            foreach (DataColumn column in dataTable.Columns)
            {
                if (string.Equals(column.ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                    return column;
            }

            return null;
        }

        #endregion
    }
}
