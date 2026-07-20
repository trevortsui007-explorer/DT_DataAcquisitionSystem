using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Common.Utilities;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using DT_DataAcquisitionSystem.Infrastructure.Repositories;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class TestAcquisitionService : ITestAcquisitionService
    {
        private const string TestMarkerPrefix = "__TST_";
        private const string TestFileModeCopy = "copy";
        private const string TestFileModeDirect = "direct";
        private const string CleanupKeyModeMarker = "FileNameMarker";
        private const string CleanupKeyModeId = "Id";
        private static readonly ConcurrentDictionary<string, TestAcquisitionRunDto> Runs =
            new ConcurrentDictionary<string, TestAcquisitionRunDto>(StringComparer.OrdinalIgnoreCase);

        private readonly IFileConfigService _configService;
        private readonly IDataAcquisitionService _dataAcquisitionService;
        private readonly IAcquisitionLogService _logService;
        private readonly ILogCodeGenerator _logCodeGenerator;
        private readonly IDataService _dataService;
        private readonly IFileProviderFactory _fileFactory;
        private readonly string _connectionString;

        public TestAcquisitionService(
            IFileConfigService configService,
            IDataAcquisitionService dataAcquisitionService,
            IAcquisitionLogService logService,
            ILogCodeGenerator logCodeGenerator,
            IDataService dataService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _dataAcquisitionService = dataAcquisitionService ?? throw new ArgumentNullException(nameof(dataAcquisitionService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _logCodeGenerator = logCodeGenerator ?? throw new ArgumentNullException(nameof(logCodeGenerator));
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _fileFactory = FileIocHelper.GetFileProviderFactory();
            _connectionString = ConfigurationManager.ConnectionStrings["BaseDb"].ConnectionString;
        }

        public async Task<TestAcquisitionRunDto> StartAsync(TestAcquisitionStartRequestDto request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.ConfigId <= 0) throw new ArgumentException("configId 必须大于 0。");

            string testRunId = DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string testFileMode = NormalizeTestFileMode(request.TestFileMode);
            bool directMode = string.Equals(testFileMode, TestFileModeDirect, StringComparison.OrdinalIgnoreCase);
            var run = new TestAcquisitionRunDto
            {
                TestRunId = testRunId,
                ConfigId = request.ConfigId,
                TestFileMode = testFileMode,
                DeleteTestFileOnCleanup = !directMode,
                CleanupKeyMode = directMode ? CleanupKeyModeId : CleanupKeyModeMarker,
                Status = "Running",
                Message = "测试采集开始。"
            };
            AddDefaultSteps(run);
            Runs[testRunId] = run;

            try
            {
                var config = _configService.GetByIds(new[] { request.ConfigId.ToString() })?.FirstOrDefault();
                if (config == null) throw new InvalidOperationException($"未找到配置：{request.ConfigId}");

                run.EqName = config.EqName;
                run.TableName = config.TableName;
                run.TestFileLocation = request.TestFileLocation;
                run.LocalTestDirectory = request.LocalTestDirectory;
                run.RunPostProcessing = request.RunPostProcessing;

                CompleteStep(run, "source", "正在校验目标表和文件源路径。");
                var cleanupColumns = await ValidateCleanupColumnsAsync(config.TableName, directMode, ct).ConfigureAwait(false);

                var sourceFile = await FindLatestSourceFileAsync(config, ct).ConfigureAwait(false);
                if (sourceFile == null || string.IsNullOrWhiteSpace(sourceFile.FullPath))
                {
                    var candidateResult = await FindSourceCandidatesAsync(config, ct).ConfigureAwait(false);
                    run.SourceCandidates = candidateResult.Candidates;
                    run.SourceCandidateError = candidateResult.ErrorMessage;
                    run.ManualSourceSelectionRequired = true;
                    run.Status = "WaitingForSourceSelection";
                    string sourceSelectionMessage = string.IsNullOrWhiteSpace(candidateResult.ErrorMessage)
                        ? "未找到可用于测试采集的源文件，请手动选择源文件。"
                        : candidateResult.ErrorMessage;
                    run.Message = sourceSelectionMessage;
                    SetStep(run, "latestFile", "Failed", sourceSelectionMessage);
                    return run;
                }

                run.SourceFilePath = sourceFile.FullPath;
                CompleteStep(run, "latestFile", $"已选择最新源文件：{sourceFile.FileName}");

                HashSet<string> beforeIds = null;
                if (directMode)
                {
                    run.TestFilePath = sourceFile.FullPath;
                    run.TestFileName = sourceFile.FileName;
                    beforeIds = await GetTargetIdsForFileAsync(config.TableName, cleanupColumns, sourceFile.FullPath, sourceFile.FileName, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    string testFilePath = await CopyTestFileAsync(config, sourceFile.FullPath, testRunId, request, ct).ConfigureAwait(false);
                    run.TestFilePath = testFilePath;
                    run.TestFileName = GetFileName(testFilePath);
                }
                CompleteStep(run, "copyFile", $"测试文件已复制：{run.TestFileName}");

                if (directMode)
                {
                    CompleteStep(run, "copyFile", "Direct source file mode; no test file copied.");
                }

                CompleteStep(run, "mapping", cleanupColumns.Count > 0
                    ? $"目标表支持安全清理字段：{string.Join(", ", cleanupColumns)}"
                    : "目标表字段校验通过。");

                await ExecuteTestAcquisitionAsync(config, run, request.RunPostProcessing, directMode, ct).ConfigureAwait(false);
                await RefreshRunDetailsAsync(run, ct).ConfigureAwait(false);
                if (directMode)
                {
                    var afterIds = await GetTargetIdsForFileAsync(config.TableName, cleanupColumns, sourceFile.FullPath, sourceFile.FileName, ct)
                        .ConfigureAwait(false);
                    run.InsertedIds = afterIds
                        .Where(x => !beforeIds.Contains(x))
                        .ToList();
                    if (run.ProcessedRows > 0 && run.InsertedIds.Count == 0)
                    {
                        throw new InvalidOperationException("Direct mode cannot safely calculate inserted Ids. Please use copy mode.");
                    }
                }
                CompleteStep(run, "execute", "测试采集执行完成。");

                await RefreshRunDetailsAsync(run, ct).ConfigureAwait(false);
                CompleteStep(run, "verify", "请人工校验测试入库数据，确认无误后清理测试数据。");

                run.Status = run.Details.Any(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                    ? "Failed"
                    : "Success";
                run.Message = run.Status == "Success" ? "测试入库完成，等待人工校验。" : "测试采集失败，可清理已产生的测试数据。";
                run.CanCleanup = true;
                run.EndTime = DateTime.Now;
                return run;
            }
            catch (Exception ex)
            {
                FailCurrentStep(run, ex.Message);
                run.Status = "Failed";
                run.Message = ex.Message;
                run.CanCleanup = !string.IsNullOrWhiteSpace(run.TestFilePath) || !string.IsNullOrWhiteSpace(run.TaskLogId);
                run.EndTime = DateTime.Now;
                return run;
            }
        }

        public async Task<TestAcquisitionRunDto> GetAsync(string testRunId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(testRunId)) throw new ArgumentNullException(nameof(testRunId));

            if (!Runs.TryGetValue(testRunId, out var run))
            {
                return null;
            }

            await RefreshRunDetailsAsync(run, ct).ConfigureAwait(false);
            return run;
        }

        public async Task<TestAcquisitionRunDto> SelectSourceAsync(string testRunId, TestAcquisitionSelectSourceRequestDto request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(testRunId)) throw new ArgumentNullException(nameof(testRunId));
            if (request == null || string.IsNullOrWhiteSpace(request.FullPath)) throw new ArgumentException("fullPath 不能为空。");

            if (!Runs.TryGetValue(testRunId, out var run))
            {
                throw new InvalidOperationException("未找到测试采集运行记录，可能服务已重启或记录已清理。");
            }

            if (!run.ManualSourceSelectionRequired &&
                !string.Equals(run.Status, "WaitingForSourceSelection", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("当前测试采集不处于手动选择源文件状态。");
            }

            var selected = (run.SourceCandidates ?? new List<FileEntryDto>())
                .FirstOrDefault(x => string.Equals(x.FullPath, request.FullPath, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                throw new InvalidOperationException("只能选择本次候选列表中的源文件。");
            }

            var config = _configService.GetByIds(new[] { run.ConfigId.ToString() })?.FirstOrDefault();
            if (config == null) throw new InvalidOperationException($"未找到配置：{run.ConfigId}");

            var credentials = ResolveCredentials(config);
            var provider = _fileFactory.Create(selected.FullPath, credentials.UserName, credentials.Password);
            if (!provider.Exists(selected.FullPath))
            {
                throw new FileNotFoundException("选择的源文件不存在或无法访问。", selected.FullPath);
            }

            run.ManualSourceSelectionRequired = false;
            run.SourceCandidateError = null;
            run.Status = "Running";
            run.Message = "已手动选择源文件，继续执行测试采集。";
            SetStep(run, "latestFile", "Success", $"已手动选择源文件：{selected.FileName}");

            return await ContinueWithSourceFileAsync(config, run, selected, ct).ConfigureAwait(false);
        }

        public async Task<TestAcquisitionCleanupResultDto> CleanupAsync(string testRunId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(testRunId)) throw new ArgumentNullException(nameof(testRunId));

            if (!Runs.TryGetValue(testRunId, out var run))
            {
                throw new InvalidOperationException("未找到测试运行记录，可能服务已重启或记录已清理。");
            }

            string marker = TestMarkerPrefix + testRunId;
            string likeMarker = "%" + marker + "%";
            var result = new TestAcquisitionCleanupResultDto
            {
                TestRunId = testRunId,
                CleanupKeyMode = run.CleanupKeyMode
            };

            try
            {
                if (run.DeleteTestFileOnCleanup && !string.IsNullOrWhiteSpace(run.TestFilePath))
                {
                    var config = _configService.GetByIds(new[] { run.ConfigId.ToString() })?.FirstOrDefault();
                    var credentials = ResolveCredentials(config);
                    var provider = _fileFactory.Create(run.TestFilePath, credentials.UserName, credentials.Password);
                    await provider.DeleteFileAsync(run.TestFilePath, ct).ConfigureAwait(false);
                    result.DeletedTestFile = true;
                }
            }
            catch (Exception ex)
            {
                result.Message = "测试文件删除失败：" + ex.Message;
            }

            if (!string.IsNullOrWhiteSpace(run.TableName))
            {
                result.DeletedTargetRows = string.Equals(run.CleanupKeyMode, CleanupKeyModeId, StringComparison.OrdinalIgnoreCase)
                    ? await DeleteTargetRowsByIdsAsync(run.TableName, run.InsertedIds, ct).ConfigureAwait(false)
                    : await DeleteTargetRowsAsync(run.TableName, likeMarker, ct).ConfigureAwait(false);
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);

                if (!string.Equals(run.CleanupKeyMode, CleanupKeyModeId, StringComparison.OrdinalIgnoreCase))
                {
                    result.DeletedFileStateRows = await conn.ExecuteAsync(
                        new CommandDefinition(
                            @"DELETE FROM [dbo].[DA_AcquisitionFileState]
                              WHERE [FileName] LIKE @Marker OR [FullPath] LIKE @Marker;",
                            new { Marker = likeMarker },
                            cancellationToken: ct)).ConfigureAwait(false);
                }

                result.DeletedLogRows = await conn.ExecuteAsync(
                    new CommandDefinition(
                        @"DELETE FROM [dbo].[DA_AcquisitionLog]
                          WHERE (@TaskLogId IS NOT NULL AND [TaskLogId] = @TaskLogId)
                             OR [FileName] LIKE @Marker
                             OR [FullFilePath] LIKE @Marker;",
                        new { TaskLogId = string.IsNullOrWhiteSpace(run.TaskLogId) ? null : run.TaskLogId, Marker = likeMarker },
                        cancellationToken: ct)).ConfigureAwait(false);

                result.DeletedTaskLogRows = await conn.ExecuteAsync(
                    new CommandDefinition(
                        @"DELETE FROM [dbo].[DA_AcquisitionTaskLog]
                          WHERE [TriggerType] = @TriggerType
                            AND ((@TaskLogId IS NOT NULL AND [Id] = @TaskLogId) OR [TaskCode] LIKE @Marker);",
                        new { TriggerType = TaskTriggerTypes.Test, TaskLogId = string.IsNullOrWhiteSpace(run.TaskLogId) ? null : run.TaskLogId, Marker = likeMarker },
                        cancellationToken: ct)).ConfigureAwait(false);
            }

            result.Success = string.IsNullOrWhiteSpace(result.Message);
            result.Message = result.Success ? "测试数据清理完成。" : result.Message;
            run.Cleaned = result.Success;
            run.CanCleanup = !result.Success;
            run.CleanupMessage = result.Message;
            CompleteStep(run, "cleanup", result.Message);

            if (result.Success)
            {
                Runs.TryRemove(testRunId, out _);
            }

            return result;
        }

        private async Task<TestAcquisitionRunDto> ContinueWithSourceFileAsync(AcquisitionConfig config, TestAcquisitionRunDto run, FileEntryDto sourceFile, CancellationToken ct)
        {
            bool directMode = string.Equals(run.TestFileMode, TestFileModeDirect, StringComparison.OrdinalIgnoreCase);
            var cleanupColumns = await ValidateCleanupColumnsAsync(config.TableName, directMode, ct).ConfigureAwait(false);

            run.SourceFilePath = sourceFile.FullPath;
            HashSet<string> beforeIds = null;
            if (directMode)
            {
                run.TestFilePath = sourceFile.FullPath;
                run.TestFileName = sourceFile.FileName;
                beforeIds = await GetTargetIdsForFileAsync(config.TableName, cleanupColumns, sourceFile.FullPath, sourceFile.FileName, ct)
                    .ConfigureAwait(false);
                CompleteStep(run, "copyFile", "Direct source file mode; no test file copied.");
            }
            else
            {
                var request = new TestAcquisitionStartRequestDto
                {
                    ConfigId = run.ConfigId,
                    TestFileMode = run.TestFileMode,
                    TestFileLocation = run.TestFileLocation,
                    LocalTestDirectory = run.LocalTestDirectory,
                    RunPostProcessing = run.RunPostProcessing
                };
                string testFilePath = await CopyTestFileAsync(config, sourceFile.FullPath, run.TestRunId, request, ct).ConfigureAwait(false);
                run.TestFilePath = testFilePath;
                run.TestFileName = GetFileName(testFilePath);
                CompleteStep(run, "copyFile", $"测试文件已复制：{run.TestFileName}");
            }

            CompleteStep(run, "mapping", cleanupColumns.Count > 0
                ? $"目标表支持安全清理字段：{string.Join(", ", cleanupColumns)}"
                : "目标表字段校验通过。");

            await ExecuteTestAcquisitionAsync(config, run, run.RunPostProcessing, directMode, ct).ConfigureAwait(false);
            await RefreshRunDetailsAsync(run, ct).ConfigureAwait(false);
            if (directMode)
            {
                var afterIds = await GetTargetIdsForFileAsync(config.TableName, cleanupColumns, sourceFile.FullPath, sourceFile.FileName, ct)
                    .ConfigureAwait(false);
                run.InsertedIds = afterIds
                    .Where(x => !beforeIds.Contains(x))
                    .ToList();
                if (run.ProcessedRows > 0 && run.InsertedIds.Count == 0)
                {
                    throw new InvalidOperationException("Direct mode cannot safely calculate inserted Ids. Please use copy mode.");
                }
            }

            CompleteStep(run, "execute", "测试采集执行完成。");
            await RefreshRunDetailsAsync(run, ct).ConfigureAwait(false);
            CompleteStep(run, "verify", "请人工校验测试入库数据，确认无误后清理测试数据。");

            run.Status = run.Details.Any(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                ? "Failed"
                : "Success";
            run.Message = run.Status == "Success" ? "测试入库完成，等待人工校验。" : "测试采集失败，可清理已产生的测试数据。";
            run.CanCleanup = true;
            run.EndTime = DateTime.Now;
            return run;
        }

        private async Task<FileEntryDto> FindLatestSourceFileAsync(AcquisitionConfig config, CancellationToken ct)
        {
            var todayExpectedFile = await TryFindExpectedSourceFileAsync(config, DateTime.Today, ct).ConfigureAwait(false);
            if (todayExpectedFile != null) return todayExpectedFile;

            var discovery = new FileDiscoveryService();
            DateTime end = DateTime.Today;
            DateTime start = end.AddDays(-31);
            var result = await discovery.GetDetailedDiscoveryAsync(config, start, end, null, null, ct).ConfigureAwait(false);

            return (result ?? new List<FileDiscoveryDto>())
                .SelectMany(x => x.Files ?? new List<FileEntryDto>())
                .Where(x => x != null && !x.IsMissing && !string.IsNullOrWhiteSpace(x.FullPath))
                .OrderByDescending(x => x.LastWriteTime ?? x.DetectedDate)
                .ThenByDescending(x => x.FileName)
                .FirstOrDefault();
        }

        private async Task<FileEntryDto> TryFindExpectedSourceFileAsync(AcquisitionConfig config, DateTime targetDate, CancellationToken ct)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.FileNamePattern)) return null;

            string folderPath = GetSourceFolderPath(config, targetDate);
            string expectedName = NormalizeExpectedFileName(
                FileDateTimeUtil.GetProcessedFileName(config, targetDate),
                NormalizeExtension(config.FileType));
            if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(expectedName)) return null;

            string expectedFullPath = CombinePath(folderPath, expectedName);
            try
            {
                var credentials = ResolveCredentials(config);
                var provider = _fileFactory.Create(folderPath, credentials.UserName, credentials.Password);

                if (!provider.Exists(expectedFullPath)) return null;

                var entry = new FileEntryDto
                {
                    FileName = expectedName,
                    FullPath = expectedFullPath,
                    DetectedDate = targetDate,
                    IsMissing = false
                };

                await AttachMetadataAsync(entry, provider, ct).ConfigureAwait(false);
                return entry;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private async Task<SourceCandidateDiscoveryResult> FindSourceCandidatesAsync(AcquisitionConfig config, CancellationToken ct)
        {
            var result = new SourceCandidateDiscoveryResult();
            if (config == null) return result;

            string folderPath = GetSourceFolderPath(config, DateTime.Today);
            if (string.IsNullOrWhiteSpace(folderPath)) return result;

            try
            {
                var credentials = ResolveCredentials(config);
                var provider = _fileFactory.Create(folderPath, credentials.UserName, credentials.Password);
                string pattern = string.IsNullOrWhiteSpace(config.FileType) ? "*.*" : "*" + NormalizeExtension(config.FileType);
                IEnumerable<string> files = await provider.GetFileNamesAsync(folderPath, pattern, false, ct).ConfigureAwait(false);

                foreach (string file in files ?? Enumerable.Empty<string>())
                {
                    string fullPath = BuildCandidateFilePath(folderPath, file);
                    var entry = new FileEntryDto
                    {
                        FileName = GetFileName(fullPath),
                        FullPath = fullPath,
                        DetectedDate = DateTime.Today,
                        IsMissing = false
                    };

                    await AttachMetadataAsync(entry, provider, ct).ConfigureAwait(false);
                    result.Candidates.Add(entry);
                }

                result.Candidates = result.Candidates
                    .OrderByDescending(x => x.LastWriteTime ?? DateTime.MinValue)
                    .ThenByDescending(x => x.FileName)
                    .Take(3)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "候选文件扫描失败：" + ex.Message;
            }

            return result;
        }

        private async Task AttachMetadataAsync(FileEntryDto entry, IFileProvider provider, CancellationToken ct)
        {
            if (entry == null || provider == null || string.IsNullOrWhiteSpace(entry.FullPath)) return;

            try
            {
                FileMetadata metadata = await provider.GetFileMetadataAsync(entry.FullPath, ct).ConfigureAwait(false);
                entry.LastWriteTime = metadata?.LastWriteTime;
                entry.FileSize = metadata?.Length;
            }
            catch
            {
                // 候选列表允许元数据缺失，选择时仍会再次校验文件存在。
            }
        }

        private string GetSourceFolderPath(AcquisitionConfig config, DateTime targetDate)
        {
            if (config == null) return null;

            string folderPath = string.IsNullOrWhiteSpace(config.FilePathPattern)
                ? null
                : FileDateTimeUtil.GetDateTimeFromBrace(config.FilePathPattern, targetDate);

            if (!string.IsNullOrWhiteSpace(folderPath)) return folderPath;

            string processedPath = FileDateTimeUtil.GetProcessedFilePath(config, targetDate);
            string fileName = FileDateTimeUtil.GetProcessedFileName(config, targetDate);
            return string.IsNullOrWhiteSpace(fileName) ? processedPath : GetDirectoryName(processedPath);
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return string.Empty;

            extension = extension.Trim();
            return extension.StartsWith(".") ? extension : "." + extension;
        }

        private static string NormalizeExpectedFileName(string expectedFileName, string extension)
        {
            string expectedName = GetFileName((expectedFileName ?? string.Empty).Replace("*", string.Empty));

            if (!string.IsNullOrEmpty(extension) &&
                !expectedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                expectedName += extension;
            }

            return expectedName;
        }

        private class SourceCandidateDiscoveryResult
        {
            public List<FileEntryDto> Candidates { get; set; } = new List<FileEntryDto>();
            public string ErrorMessage { get; set; }
        }

        private async Task<string> CopyTestFileAsync(AcquisitionConfig config, string sourcePath, string testRunId, TestAcquisitionStartRequestDto request, CancellationToken ct)
        {
            var credentials = ResolveCredentials(config);
            var sourceProvider = _fileFactory.Create(sourcePath, credentials.UserName, credentials.Password);
            string sourceFileName = GetFileName(sourcePath);
            string testFileName = BuildTestFileName(sourceFileName, testRunId);

            string destinationFolder;
            if (string.Equals(request.TestFileLocation, "local", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.LocalTestDirectory))
                {
                    throw new InvalidOperationException("选择本机路径时必须填写本机目录。");
                }

                destinationFolder = request.LocalTestDirectory.Trim();
            }
            else
            {
                destinationFolder = GetDirectoryName(sourcePath);
            }

            string testFilePath = CombinePath(destinationFolder, testFileName);
            var destinationProvider = _fileFactory.Create(testFilePath, credentials.UserName, credentials.Password);

            using (var stream = await sourceProvider.GetFileStreamAsync(sourcePath, ct).ConfigureAwait(false))
            {
                await destinationProvider.SaveFileAsync(testFilePath, stream, true, ct).ConfigureAwait(false);
            }

            return testFilePath;
        }

        private async Task ExecuteTestAcquisitionAsync(AcquisitionConfig sourceConfig, TestAcquisitionRunDto run, bool runPostProcessing, bool directMode, CancellationToken ct)
        {
            var taskLogEntry = new AcquisitionTaskLogEntry
            {
                TaskId = 0,
                TaskCode = (await _logCodeGenerator.GenerateTaskCodeAsync(TaskTriggerTypes.Test, ct).ConfigureAwait(false)) + "-" + run.TestRunId,
                TriggerType = TaskTriggerTypes.Test,
                StartTime = DateTime.Now,
                Status = "Running",
                TotalConfigs = 1,
                SuccessCount = 0,
                FailureCount = 0,
                ProcessedCount = 0,
                Progress = 0,
                Message = "测试采集任务已创建。"
            };

            run.TaskLogId = await _logService.RecordTaskLogEntryAsync(taskLogEntry, ct).ConfigureAwait(false);

            var testConfig = CloneConfigForTest(sourceConfig, run.TestFilePath, runPostProcessing);
            var testOptions = directMode
                ? new AcquisitionTestExecutionOptions
                {
                    ForceConfiguredStartRow = true,
                    SkipFileStateUpdate = true,
                    BypassSealedSkip = true,
                    DisableFullReload = true
                }
                : null;
            try
            {
                await _dataAcquisitionService
                    .ProcessSingleConfig(testConfig, DateTime.Today, run.TaskLogId, ct, FileStateUpdateSources.ManualRepair, testOptions)
                    .ConfigureAwait(false);

                await _logService.CompleteTaskAsync(
                    run.TaskLogId,
                    "Success",
                    1,
                    1,
                    0,
                    "测试采集完成。",
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await RefreshRunDetailsAsync(run, ct).ConfigureAwait(false);
                int successCount = run.Details.Count(x => string.Equals(x.Status, "Success", StringComparison.OrdinalIgnoreCase));
                int failureCount = Math.Max(1, run.Details.Count(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase)));

                await _logService.CompleteTaskAsync(
                    run.TaskLogId,
                    successCount > 0 ? "PartialSuccess" : "Failed",
                    1,
                    successCount,
                    failureCount,
                    "测试采集失败：" + ex.Message,
                    ct).ConfigureAwait(false);

                throw;
            }
        }

        private AcquisitionConfig CloneConfigForTest(AcquisitionConfig source, string testFilePath, bool runPostProcessing)
        {
            var result = new AcquisitionConfig
            {
                Id = source.Id,
                EqName = source.EqName,
                TableName = source.TableName,
                FilePathPattern = GetDirectoryName(testFilePath),
                FileNamePattern = GetFileName(testFilePath),
                FileType = string.IsNullOrWhiteSpace(source.FileType) ? Path.GetExtension(testFilePath) : source.FileType,
                HeaderRow = source.HeaderRow,
                StartRow = source.StartRow,
                FieldMappings = source.FieldMappings,
                ExtFields = source.ExtFields,
                IsEnabled = true,
                PostProcessingType = runPostProcessing ? source.PostProcessingType : PostProcessingType.None,
                PostTableName = runPostProcessing ? source.PostTableName : null,
                ProcedureName = runPostProcessing ? source.ProcedureName : null,
                ServiceName = runPostProcessing ? source.ServiceName : null,
                Flag = runPostProcessing ? source.Flag : null,
                FlagName = source.FlagName,
                ParserType = source.ParserType,
                TemplateId = source.TemplateId,
                ParserOptions = source.ParserOptions,
                CreateTime = source.CreateTime
            };

            return result;
        }

        private async Task<List<string>> ValidateCleanupColumnsAsync(string tableName, bool requireId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new InvalidOperationException("目标表名为空，无法测试采集。");

            DataTable schema = await _dataService.GetTableSchemaAsync(tableName).ConfigureAwait(false);
            if (schema == null)
            {
                throw new InvalidOperationException($"目标表不存在或无法读取结构：{tableName}");
            }

            var allColumns = schema.Columns
                .Cast<DataColumn>()
                .Select(x => x.ColumnName)
                .ToList();

            if (requireId && !allColumns.Any(x => string.Equals(x, "Id", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Direct mode requires target table {tableName} to have an Id field.");
            }

            var columns = allColumns
                .Where(x => string.Equals(x, "fileName", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x, "excelname", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (columns.Count == 0)
            {
                throw new InvalidOperationException($"目标表 {tableName} 缺少 fileName 或 excelname 字段，无法安全清理测试数据。");
            }

            return columns;
        }

        private async Task<int> DeleteTargetRowsAsync(string tableName, string marker, CancellationToken ct)
        {
            DataTable schema = await _dataService.GetTableSchemaAsync(tableName).ConfigureAwait(false);
            var columns = schema.Columns
                .Cast<DataColumn>()
                .Select(x => x.ColumnName)
                .Where(x => string.Equals(x, "fileName", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x, "excelname", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (columns.Count == 0) return 0;

            string predicate = string.Join(" OR ", columns.Select(x => QuoteName(x) + " LIKE @Marker"));
            string sql = $"DELETE FROM {QuoteTableName(tableName)} WHERE {predicate};";

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                return await conn.ExecuteAsync(new CommandDefinition(sql, new { Marker = marker }, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        private async Task<HashSet<string>> GetTargetIdsForFileAsync(string tableName, List<string> fileColumns, string fullPath, string fileName, CancellationToken ct)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (fileColumns == null || fileColumns.Count == 0) return result;

            string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string predicate = string.Join(" OR ", fileColumns.Select(x => QuoteName(x) + " IN @FileValues"));
            string sql = $"SELECT CONVERT(NVARCHAR(50), [Id]) FROM {QuoteTableName(tableName)} WHERE {predicate};";
            var fileValues = new[] { fullPath, fileName, nameWithoutExtension }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                var ids = await conn.QueryAsync<string>(new CommandDefinition(sql, new { FileValues = fileValues }, cancellationToken: ct))
                    .ConfigureAwait(false);
                foreach (var id in ids.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        private async Task<int> DeleteTargetRowsByIdsAsync(string tableName, IEnumerable<string> ids, CancellationToken ct)
        {
            var idList = (ids ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (idList.Length == 0) return 0;

            string sql = $"DELETE FROM {QuoteTableName(tableName)} WHERE CONVERT(NVARCHAR(50), [Id]) IN @Ids;";
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                return await conn.ExecuteAsync(new CommandDefinition(sql, new { Ids = idList }, cancellationToken: ct))
                    .ConfigureAwait(false);
            }
        }

        private async Task RefreshRunDetailsAsync(TestAcquisitionRunDto run, CancellationToken ct)
        {
            if (run == null || string.IsNullOrWhiteSpace(run.TaskLogId)) return;

            var logs = await _logService.GetLogsByTaskLogIdAsync(run.TaskLogId, ct).ConfigureAwait(false);
            run.Details = (logs ?? new List<AcquisitionLogEntry>())
                .Select(x => new TaskDetailLogDto
                {
                    Id = x.Id,
                    TaskLogId = x.TaskLogId,
                    ConfigId = x.ConfigId,
                    FileName = x.FileName,
                    FullFilePath = x.FullFilePath,
                    StartRow = x.StartRow,
                    ProcessedRows = x.ProcessedRows,
                    StartTime = x.StartTime,
                    EndTime = x.EndTime,
                    Status = x.Status,
                    ErrorMessage = x.ErrorMessage
                })
                .ToList();
            run.ProcessedRows = run.Details.Sum(x => Math.Max(0, x.ProcessedRows));
        }

        private FileAccessCredentials ResolveCredentials(AcquisitionConfig config)
        {
            FileAccessOptions access = FileAccessOptions.FromParserOptions(config?.ParserOptions);
            if (access.UseCurrentWindowsIdentity)
            {
                return new FileAccessCredentials
                {
                    UserName = null,
                    Password = null
                };
            }

            return new FileAccessCredentials
            {
                UserName = access.HasUserName ? access.EffectiveUserName : null,
                Password = access.HasPassword ? access.GetPassword() : null
            };
        }

        private static void AddDefaultSteps(TestAcquisitionRunDto run)
        {
            AddStep(run, 1, "source", "文件源路径通断");
            AddStep(run, 2, "latestFile", "自动选择最新源文件");
            AddStep(run, 3, "copyFile", "复制测试文件");
            AddStep(run, 4, "mapping", "校验模板/字段映射");
            AddStep(run, 5, "execute", "执行测试采集");
            AddStep(run, 6, "verify", "人工校验入库结果");
            AddStep(run, 7, "cleanup", "清理测试数据");
        }

        private static void AddStep(TestAcquisitionRunDto run, int order, string code, string name)
        {
            run.Steps.Add(new TestAcquisitionStepDto
            {
                Order = order,
                Code = code,
                Name = name,
                Status = "Pending"
            });
        }

        private static void CompleteStep(TestAcquisitionRunDto run, string code, string message)
        {
            var step = run.Steps.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            if (step == null) return;
            step.Status = "Success";
            step.Message = message;
        }

        private static void SetStep(TestAcquisitionRunDto run, string code, string status, string message)
        {
            var step = run.Steps.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            if (step == null) return;
            step.Status = status;
            step.Message = message;
        }

        private static void FailCurrentStep(TestAcquisitionRunDto run, string message)
        {
            var step = run.Steps.FirstOrDefault(x => x.Status == "Pending") ??
                       run.Steps.LastOrDefault(x => x.Status == "Running") ??
                       run.Steps.LastOrDefault(x => x.Status == "Success");
            if (step == null) return;
            step.Status = "Failed";
            step.Message = message;
        }

        private static string BuildTestFileName(string sourceFileName, string testRunId)
        {
            string extension = Path.GetExtension(sourceFileName);
            string name = Path.GetFileNameWithoutExtension(sourceFileName);
            return name + TestMarkerPrefix + testRunId + extension;
        }

        private static string GetFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            int index = normalized.LastIndexOf('/');
            return index >= 0 ? normalized.Substring(index + 1) : Path.GetFileName(path);
        }

        private static string GetDirectoryName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (path.Contains("://"))
            {
                string trimmed = path.TrimEnd('/');
                int index = trimmed.LastIndexOf('/');
                return index > 0 ? trimmed.Substring(0, index) : trimmed;
            }

            return Path.GetDirectoryName(path);
        }

        private static string CombinePath(string folder, string file)
        {
            if (string.IsNullOrWhiteSpace(folder)) return file;
            if (string.IsNullOrWhiteSpace(file)) return folder;

            string trimmedFolder = folder.TrimEnd('/', '\\');
            string trimmedFile = file.TrimStart('/', '\\');
            if (trimmedFolder.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                trimmedFolder.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmedFolder.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedFolder + "/" + trimmedFile.Replace('\\', '/');
            }

            return trimmedFolder + "\\" + trimmedFile.Replace('/', '\\');
        }

        private static string BuildCandidateFilePath(string folder, string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return file;
            if (file.Contains("://") || Path.IsPathRooted(file) || file.StartsWith(@"\\"))
            {
                return file;
            }

            return CombinePath(folder, file);
        }

        private static string QuoteTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName) || !Regex.IsMatch(tableName, @"^[A-Za-z0-9_\.]+$"))
            {
                throw new InvalidOperationException("目标表名包含非法字符，拒绝执行清理。");
            }

            return string.Join(".", tableName.Split('.').Select(QuoteName));
        }

        private static string QuoteName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"^[A-Za-z0-9_]+$"))
            {
                throw new InvalidOperationException("数据库字段名包含非法字符，拒绝执行清理。");
            }

            return "[" + name + "]";
        }

        private static string NormalizeTestFileMode(string mode)
        {
            return string.Equals(mode, TestFileModeDirect, StringComparison.OrdinalIgnoreCase)
                ? TestFileModeDirect
                : TestFileModeCopy;
        }

        private class FileAccessCredentials
        {
            public string UserName { get; set; }
            public string Password { get; set; }
        }
    }
}
