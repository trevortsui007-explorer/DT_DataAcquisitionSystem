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
using DT_DataAcquisitionSystem.Infrastructure;
using DT_DataAcquisitionSystem.Infrastructure.Repositories;
using Newtonsoft.Json.Linq;

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
        private readonly IImportTemplateService _importTemplateService;

        // 并发控制：防止同时处理过多任务撑爆数据库连接池
        private readonly SemaphoreSlim _concurrencySemaphore = new SemaphoreSlim(5);
        // 内存级文件指纹去重（防止同一周期内重复读取）
        private readonly ConcurrentHashSet<string> _processingFiles = new ConcurrentHashSet<string>();

        // FTP 服务器凭证
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
            _importTemplateService = new ImportTemplateService(new ImportTemplateRepository());
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
            var provider = CreateProvider(config, path);

            if (!provider.Exists(path))
            {
                throw new FileNotFoundException($"未找到指定文件: {path}", path);
            }

            IEnumerable<Dictionary<string, object>> fullData;

            // 2. 获取并解析文件流
            using (var stream = await provider.GetFileStreamAsync(path, ct))
            {
                fullData = await ParseFileDataAsync(config, stream, Path.GetFileName(path), path, config.StartRow, ct)
                    .ConfigureAwait(false);
            }

            // 3. 预处理：使数据字段和数据库表字段对应
            FileMetadata fileMetadata = await GetFileMetadataSafeAsync(provider, path, ct).ConfigureAwait(false);
            var processedData = fullData
                .Select(row => ApplyConfiguredFields(DataMapperUtil.MapRow(row, config.FieldMappings), config, fileMetadata))
                .ToList();

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

            string path = null;
            string filename = null;
            bool hasStartedFileProcessing = false;

            try
            {
                // 1. 解析路径和文件名
                path = FileDateTimeUtil.GetProcessedFilePath(config, processDate);   // 如果fileName为空，会返回folderPath；非空返回完整路径

                filename = FileDateTimeUtil.GetProcessedFileName(config, processDate);

                var provider = CreateProvider(config, path);
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
                    var scanOptions = FolderScanOptionsUtil.FromConfig(config);
                    var files = await FolderScanOptionsUtil.GetFilesAsync(provider, path, pattern, scanOptions, ct)
                        .ConfigureAwait(false);
                    if (files != null && files.Any())
                    {
                        foreach (var file in files)
                        {
                            targetFiles.Add(BuildTargetFilePath(path, file));
                        }
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
                        hasStartedFileProcessing = true;
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
            catch (Exception ex)
            {
                if (!hasStartedFileProcessing)
                {
                    await RecordConfigFailureLogAsync(config, processDate, taskLogId, path, filename, ex, ct)
                        .ConfigureAwait(false);
                }

                throw;
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

            // 1. 初始化明细日志（每个物理文件一条记录）
            var logEntry = new AcquisitionLogEntry
            {
                TaskLogId = taskLogId,
                ConfigId = config.Id,
                BusinessDate = businessDate.Date,
                FileName = actualFileName,
                FullFilePath = filePath,
                StartTime = DateTime.Now,
                Status = "Running"
            };

            try
            {
                // 2. 防重与断点续传检查
                FileMetadata fileMetadata = await GetFileMetadataSafeAsync(provider, filePath, ct).ConfigureAwait(false);
                AcquisitionFileState fileState = await _fileStateService.GetAsync(config.Id, businessDate, actualFileName, ct).ConfigureAwait(false);
                AcquisitionModeOptions acquisitionMode = ReadAcquisitionModeOptions(config);
                bool shouldFullReload = ShouldFullReload(acquisitionMode, fileState, fileMetadata);

                if (!shouldFullReload && await _fileStateService.ShouldSkipForSealedAsync(config.Id, businessDate, actualFileName, updateSource, ct).ConfigureAwait(false))
                {
                    return;
                }

                startRow = shouldFullReload
                    ? ResolveConfiguredStartRow(config)
                    : await _logService.GetNextStartRowAsync(config.Id, businessDate, actualFileName, ct);
                logEntry.StartRow = startRow;

                if (!provider.Exists(filePath))
                {
                    throw new FileNotFoundException($"文件未找到: {filePath}");
                }

                // 3. 获取并解析文件流
                IEnumerable<Dictionary<string, object>> fullData;
                using (var stream = await provider.GetFileStreamAsync(filePath, ct))
                {
                    fullData = await ParseFileDataAsync(config, stream, actualFileName, filePath, startRow, ct)
                        .ConfigureAwait(false);
                }

                // 4. 数据预处理
                var fullDataList = (fullData ?? Enumerable.Empty<Dictionary<string, object>>()).ToList();
                int parsedStartRow = GetParsedStartRow(fullDataList);
                if (parsedStartRow > 0)
                {
                    startRow = parsedStartRow;
                    logEntry.StartRow = parsedStartRow;
                }

                var processedData = fullDataList
                    .Select(row => ApplyConfiguredFields(DataMapperUtil.MapRow(row, config.FieldMappings), config, fileMetadata))
                    .ToList();
                processedRows = processedData.Count;

                // 5. 入库
                if (processedRows > 0 || shouldFullReload)
                {
                    DataTable schema = await _dataService.GetTableSchemaAsync(config.TableName);
                    if (schema == null) throw new InvalidOperationException($"表 {config.TableName} 架构不存在");

                    DataTable dataToInsert = _dataService.PopulateDataTable(processedData, schema);
                    var postProcessingRows = ExtractPostProcessingRowKeys(dataToInsert);
                    if (shouldFullReload)
                    {
                        await _dataService.ReplaceFileDataAsync(dataToInsert, config.TableName, filePath, actualFileName, businessDate, ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await _dataService.BulkInsertAsync(dataToInsert, config.TableName, ct);
                    }

                    // 6. 执行数据后处理 0 - 不处理；1 - 使用存储过程处理； 2- 使用C# Service处理
                    if (processedRows > 0)
                    {
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
                            throw new Exception("Post processing failed: " + ex.Message, ex);
                        }
                    }
                }

                // 7. 成功：完善并写入明细日志
                logEntry.ProcessedRows = processedRows;
                logEntry.EndTime = DateTime.Now;
                logEntry.Status = "Success";
                await _logService.RecordLogEntryAsync(logEntry, ct);
                await _fileStateService.UpsertSuccessAsync(config, businessDate, filePath, logEntry, updateSource, fileMetadata, shouldFullReload, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 失败：记录错误到明细日志
                logEntry.EndTime = DateTime.Now;
                logEntry.Status = "Failed";
                logEntry.ErrorMessage = TruncateErrorMessage(ex.Message);
                logEntry.ProcessedRows = processedRows;

                await _logService.RecordLogEntryAsync(logEntry, ct);

                // 继续向上抛出，让外层的 foreach 捕获（如果是文件夹模式，会被外层吃掉异常继续下一个；如果是单文件，可按需处理）
                throw new Exception($"处理文件 {filePath} 失败: {ex.Message}", ex);
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

            string finalMessage = BuildFinalMessage(finalStatus, summary);

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

        private async Task RecordConfigFailureLogAsync(AcquisitionConfig config, DateTime processDate, string taskLogId, string path, string filename, Exception ex, CancellationToken ct)
        {
            if (config == null || string.IsNullOrWhiteSpace(taskLogId)) return;

            DateTime now = DateTime.Now;
            var logEntry = new AcquisitionLogEntry
            {
                TaskLogId = taskLogId,
                ConfigId = config.Id,
                BusinessDate = processDate.Date,
                FileName = ResolveConfigFailureFileName(path, filename),
                FullFilePath = ResolveConfigFailureFullFilePath(path, filename),
                StartRow = 0,
                ProcessedRows = 0,
                StartTime = now,
                EndTime = now,
                Status = "Failed",
                ErrorMessage = TruncateErrorMessage(ex?.Message)
            };

            await _logService.RecordLogEntryAsync(logEntry, ct).ConfigureAwait(false);
        }

        private static string ResolveConfigFailureFileName(string path, string filename)
        {
            if (!string.IsNullOrWhiteSpace(filename)) return filename;
            if (string.IsNullOrWhiteSpace(path)) return "\u914d\u7f6e\u7ea7\u5931\u8d25";

            string trimmed = path.TrimEnd('/', '\\');
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? "\u914d\u7f6e\u7ea7\u5931\u8d25" : name;
        }

        private static string ResolveConfigFailureFullFilePath(string path, string filename)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (string.IsNullOrWhiteSpace(filename)) return path;

            try
            {
                if (string.Equals(Path.GetFileName(path), filename, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                return Path.Combine(path, filename);
            }
            catch
            {
                return path.TrimEnd('/', '\\') + Path.DirectorySeparatorChar + filename;
            }
        }

        private static string BuildFinalMessage(string finalStatus, AcquisitionSummary summary)
        {
            string message;

            if (finalStatus == "Success")
            {
                message = "\u4efb\u52a1\u5b8c\u6210";
            }
            else if (finalStatus == "Failed")
            {
                message = $"\u4efb\u52a1\u5b8c\u6210\uff0c\u4f46\u5168\u90e8\u5931\u8d25\u3002\u5931\u8d25\u6570\uff1a{summary.FailureCount}";
            }
            else if (finalStatus == "PartialSuccess")
            {
                message = $"\u4efb\u52a1\u5b8c\u6210\uff0c\u90e8\u5206\u5931\u8d25\u3002\u6210\u529f\u6570\uff1a{summary.SuccessCount}\uff0c\u5931\u8d25\u6570\uff1a{summary.FailureCount}";
            }
            else
            {
                message = "\u6ca1\u6709\u53ef\u6267\u884c\u7684\u6570\u636e\u3002";
            }

            if ((finalStatus == "Failed" || finalStatus == "PartialSuccess") && summary.ErrorDetails.Any())
            {
                string reason = string.Join("\uff1b", summary.ErrorDetails.Take(3));
                message = $"{message}\u3002\u539f\u56e0\uff1a{reason}";
            }

            return TruncateTaskMessage(message);
        }

        private static string TruncateTaskMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;
            return message.Length > 500 ? message.Substring(0, 500) : message;
        }

        private static string TruncateErrorMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return message;
            return message.Length > 1000 ? message.Substring(0, 1000) : message;
        }

        private static string ResolveManualUpdateSource(DateTime startDate, DateTime endDate)
        {
            return endDate.Date < DateTime.Today
                ? FileStateUpdateSources.ManualRepair
                : FileStateUpdateSources.ManualCurrent;
        }

        private IFileProvider CreateProvider(AcquisitionConfig config, string path)
        {
            FileAccessOptions access = FileAccessOptions.FromParserOptions(config?.ParserOptions);
            string userName = access.HasUserName ? access.EffectiveUserName : null;
            string password = access.HasPassword ? access.GetPassword() : null;
            return _fileFactory.Create(path, userName, password);
        }

        private class AcquisitionModeOptions
        {
            public bool IsFullReloadMode { get; set; }
            public bool FullReloadWhenLastWriteTimeChanged { get; set; }
            public bool FullReloadWhenFileSizeChanged { get; set; }
        }

        private static AcquisitionModeOptions ReadAcquisitionModeOptions(AcquisitionConfig config)
        {
            var result = new AcquisitionModeOptions();
            JObject options = ParseParserOptions(config?.ParserOptions);
            JObject acquisitionMode = GetObjectIgnoreCase(options, "acquisitionMode");
            if (acquisitionMode == null) return result;

            string mode = GetStringIgnoreCase(acquisitionMode, "mode");
            result.IsFullReloadMode = string.Equals(mode, "full-reload", StringComparison.OrdinalIgnoreCase);
            result.FullReloadWhenLastWriteTimeChanged = GetBoolIgnoreCase(acquisitionMode, "fullReloadWhenLastWriteTimeChanged");
            result.FullReloadWhenFileSizeChanged = GetBoolIgnoreCase(acquisitionMode, "fullReloadWhenFileSizeChanged");
            return result;
        }

        private static bool ShouldFullReload(AcquisitionModeOptions options, AcquisitionFileState state, FileMetadata metadata)
        {
            if (options == null || !options.IsFullReloadMode || state == null || metadata == null)
                return false;

            if (options.FullReloadWhenLastWriteTimeChanged && IsDateChanged(state.LastWriteTime, metadata.LastWriteTime))
                return true;

            if (options.FullReloadWhenFileSizeChanged && IsLongChanged(state.FileSize, metadata.Length))
                return true;

            return false;
        }

        private static bool IsDateChanged(DateTime? oldValue, DateTime? newValue)
        {
            if (!newValue.HasValue) return false;
            if (!oldValue.HasValue) return true;

            return Math.Abs((oldValue.Value - newValue.Value).TotalSeconds) >= 1;
        }

        private static bool IsLongChanged(long? oldValue, long? newValue)
        {
            if (!newValue.HasValue) return false;
            if (!oldValue.HasValue) return true;

            return oldValue.Value != newValue.Value;
        }

        private static int ResolveConfiguredStartRow(AcquisitionConfig config)
        {
            return config?.StartRow > 0 ? config.StartRow : 1;
        }

        private static async Task<FileMetadata> GetFileMetadataSafeAsync(IFileProvider provider, string filePath, CancellationToken ct)
        {
            try
            {
                return await provider.GetFileMetadataAsync(filePath, ct).ConfigureAwait(false) ?? new FileMetadata();
            }
            catch
            {
                return new FileMetadata();
            }
        }

        private static Dictionary<string, object> ApplyConfiguredFields(
            Dictionary<string, object> row,
            AcquisitionConfig config,
            FileMetadata metadata)
        {
            if (row == null) row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            JObject options = ParseParserOptions(config?.ParserOptions);
            if (options == null) return row;

            JObject systemFields = GetObjectIgnoreCase(options, "systemFields");
            if (systemFields != null)
            {
                ApplySystemField(row, systemFields, "fileLastWriteTime", metadata?.LastWriteTime);
                ApplySystemField(row, systemFields, "fileLastWriteTimeUtc", metadata?.LastWriteTimeUtc);
                ApplySystemField(row, systemFields, "fileSize", metadata?.Length);
            }

            JObject fixedFields = GetObjectIgnoreCase(options, "fixedFields");
            if (fixedFields != null)
            {
                foreach (var property in fixedFields.Properties())
                {
                    if (string.IsNullOrWhiteSpace(property.Name)) continue;
                    row[property.Name] = ConvertJTokenValue(property.Value);
                }
            }

            return row;
        }

        private static JObject ParseParserOptions(string parserOptions)
        {
            if (string.IsNullOrWhiteSpace(parserOptions)) return null;

            try
            {
                return JObject.Parse(parserOptions);
            }
            catch
            {
                return null;
            }
        }

        private static JObject GetObjectIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName)) return null;

            foreach (var property in source.Properties())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value as JObject;
                }
            }

            return null;
        }

        private static void ApplySystemField(Dictionary<string, object> row, JObject systemFields, string optionName, object value)
        {
            string targetField = GetStringIgnoreCase(systemFields, optionName);
            if (string.IsNullOrWhiteSpace(targetField)) return;

            row[targetField] = value;
        }

        private static string GetStringIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName)) return null;

            foreach (var property in source.Properties())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToString(ConvertJTokenValue(property.Value));
                }
            }

            return null;
        }

        private static bool GetBoolIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName)) return false;

            foreach (var property in source.Properties())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.Value == null) return false;
                if (property.Value.Type == JTokenType.Boolean) return property.Value.Value<bool>();

                bool result;
                return bool.TryParse(Convert.ToString(ConvertJTokenValue(property.Value)), out result) && result;
            }

            return false;
        }

        private static object ConvertJTokenValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return null;

            if (token.Type == JTokenType.Integer) return token.Value<long>();
            if (token.Type == JTokenType.Float) return token.Value<decimal>();
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            if (token.Type == JTokenType.Date) return token.Value<DateTime>();

            return token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString();
        }

        private async Task<IEnumerable<Dictionary<string, object>>> ParseFileDataAsync(
            AcquisitionConfig config,
            Stream stream,
            string fileName,
            string fullPath,
            int startRow,
            CancellationToken ct)
        {
            if (IsTemplateExcelConfig(config))
            {
                if (!config.TemplateId.HasValue || config.TemplateId.Value <= 0)
                {
                    throw new InvalidOperationException($"Config {config.EqName} uses template parser but TemplateId is empty.");
                }

                var template = _importTemplateService.GetById(config.TemplateId.Value);
                if (template == null)
                {
                    throw new InvalidOperationException($"Import template not found: {config.TemplateId.Value}");
                }

                var templateParser = new TemplateExcelParser();
                return await templateParser.ParseAsync(stream, template, config, fileName, fullPath, startRow, ct)
                    .ConfigureAwait(false);
            }

            string ext = Path.GetExtension(fullPath);
            var parser = DataParserIocHelper.GetParser(ext);
            ParserOptionsBase options = DataParserIocHelper.CreateOptions(
                ext,
                fullPath,
                headerRow: config?.HeaderRow > 0 ? config.HeaderRow : 1,
                startRow: startRow > 0 ? startRow : ResolveConfiguredStartRow(config),
                hasExtFields: !string.IsNullOrWhiteSpace(config.ExtFields),
                extFields: config.ExtFields);

            return await parser.ParseAsync<Dictionary<string, object>>(stream, options, ct)
                .ConfigureAwait(false);
        }

        private static bool IsTemplateExcelConfig(AcquisitionConfig config)
        {
            return string.Equals(config?.ParserType, "template-excel", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetParsedStartRow(IEnumerable<Dictionary<string, object>> rows)
        {
            if (rows == null) return 0;

            int minRow = 0;
            foreach (var row in rows)
            {
                if (row == null || !row.TryGetValue("SourceRow", out object value)) continue;
                if (!int.TryParse(Convert.ToString(value), out int sourceRow) || sourceRow <= 0) continue;

                if (minRow == 0 || sourceRow < minRow)
                {
                    minRow = sourceRow;
                }
            }

            return minRow;
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

        private static string BuildTargetFilePath(string folderPath, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return filePath;
            if (IsFullProviderPath(filePath)) return filePath;
            if (string.IsNullOrWhiteSpace(folderPath)) return filePath;

            return folderPath.TrimEnd('/', '\\') + "/" + filePath.TrimStart('/', '\\');
        }

        private static bool IsFullProviderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.Contains("://")) return true;

            return Path.IsPathRooted(path);
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
