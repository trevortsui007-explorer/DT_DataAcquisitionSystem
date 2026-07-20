using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Common.Utilities;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class FileDiscoveryService
    {
        private const int MetadataConcurrency = 4;
        private readonly IFileProviderFactory _factory;
        private readonly IAcquisitionFileStateService _fileStateService;

        public FileDiscoveryService()
        {
            _factory = FileIocHelper.GetFileProviderFactory();
            _fileStateService = new AcquisitionFileStateService();
        }

        /// <summary>
        /// 获取文件夹下含有的文件名（用于处理文件名不规则，文件夹多文件情况）
        /// </summary>
        public async Task<List<string>> GetScanResultsAsync(string path, string extension, string user = "", string pass = "")
        {
            // 1. 获取 Provider (内部已自动判定 FTP 或 Local)
            var provider = _factory.Create(path, user, pass);

            // 2. 调用标准接口获取列表
            // 修正 searchPattern 格式，例如 ".txt" -> "*.txt"
            string pattern = extension.StartsWith("*") ? extension : "*" + extension;
            var files = await provider.GetFileNamesAsync(path, pattern);

            // 3. 业务处理：返回不带后缀的文件名 (如原 FileScanner 逻辑)
            return files.Select(Path.GetFileNameWithoutExtension).ToList();
        }


        /// <summary>
        /// 深度巡检：根据配置的时间范围，列出所有匹配的文件清单，并标记缺失项
        /// </summary>
        /// <param name="config">采集配置（内含路径和文件名模板）</param>
        /// <param name="startDate">巡检开始日期</param>
        /// <param name="endDate">巡检结束日期</param>
        /// <param name="user">FTP 用户名 (可选)</param>
        /// <param name="pass">FTP 密码 (可选)</param>
        public async Task<List<FileDiscoveryDto>> GetDetailedDiscoveryAsync(
            AcquisitionConfig config, 
            DateTime startDate, 
            DateTime endDate, 
            string user = "", 
            string pass = "",
            CancellationToken ct = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var stopwatch = Stopwatch.StartNew();
            var resultList = new List<FileDiscoveryDto>();
            var fileStates = await _fileStateService
                .GetByConfigAndDateRangeAsync(config.Id, startDate, endDate, ct)
                .ConfigureAwait(false);
            var fileStateMap = fileStates
                .GroupBy(x => BuildStateKey(x.BusinessDate, x.FileName))
                .ToDictionary(x => x.Key, x => x.OrderByDescending(s => s.UpdateTime).First());

            var folderFilesCache = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);
            var credentials = ResolveCredentials(config, user, pass);
            bool isFolderMode = string.IsNullOrWhiteSpace(config.FileNamePattern);
            bool hasPathDayGranularity = HasDayGranularity(config.FilePathPattern);
            bool hasMonthGranularity = HasMonthGranularity(config.FilePathPattern);
            bool hasDayGranularity = hasPathDayGranularity ||
                (!isFolderMode && HasDayGranularity(config.FileNamePattern));
            string discoveryMode = isFolderMode && !hasDayGranularity && !hasMonthGranularity ? "folder-list" : "calendar";
            string extension = NormalizeExtension(config.FileType);

            // 1. 按月组织返回结构，按实际解析出的目录路径缓存扫描结果
            for (var monthDate = new DateTime(startDate.Year, startDate.Month, 1); 
                 monthDate <= endDate; 
                 monthDate = monthDate.AddMonths(1))
            {
                ct.ThrowIfCancellationRequested();
                var monthDto = new FileDiscoveryDto
                {
                    MonthName = monthDate.ToString("yyyy-MM"),
                    FolderPath = string.Empty,
                    DiscoveryMode = discoveryMode,
                    HasDayGranularity = hasDayGranularity,
                    Files = new List<FileEntryDto>()
                };

                if (discoveryMode == "folder-list")
                {
                    string actualFolderPath = FileDateTimeUtil.GetDateTimeFromBrace(config.FilePathPattern, monthDate);
                    monthDto.FolderPath = actualFolderPath;

                    var provider = _factory.Create(actualFolderPath, credentials.UserName, credentials.Password);
                    var scanOptions = FolderScanOptionsUtil.FromConfig(config);
                    IEnumerable<string> existingFiles;

                    try
                    {
                        existingFiles = await FolderScanOptionsUtil.GetFilesAsync(provider, actualFolderPath, "*", scanOptions, ct)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Discovery][Warning] Scan folder-list failed. ConfigId={config.Id}, EqName={config.EqName}, Folder={actualFolderPath}, Error={ex.Message}");
                        existingFiles = Enumerable.Empty<string>();
                    }

                    var entries = existingFiles
                        .Where(f => HasExpectedExtension(Path.GetFileName(f), extension))
                        .Select(file =>
                        {
                            string fullPath = BuildDetectedFilePath(actualFolderPath, file);
                            string actualFileName = Path.GetFileName(file);
                            return new FileEntryDto
                            {
                                FileName = actualFileName,
                                FullPath = fullPath,
                                DetectedDate = monthDate.Date,
                                IsMissing = false
                            };
                        })
                        .ToList();

                    await AttachFileMetadataAsync(entries, provider, ct).ConfigureAwait(false);
                    foreach (var entry in entries)
                    {
                        AttachFileState(entry, fileStateMap);
                        monthDto.Files.Add(entry);
                    }

                    monthDto.Files = monthDto.Files
                        .OrderByDescending(x => x.LastWriteTime ?? DateTime.MinValue)
                        .ThenBy(x => x.FileName)
                        .ToList();

                    resultList.Add(monthDto);
                    continue;
                }

                if (isFolderMode && !hasPathDayGranularity && hasMonthGranularity)
                {
                    await AppendMonthlyFolderCalendarFilesAsync(
                        config,
                        monthDto,
                        monthDate,
                        startDate,
                        endDate,
                        extension,
                        credentials,
                        fileStateMap,
                        ct).ConfigureAwait(false);

                    if (monthDto.Files.Any())
                    {
                        resultList.Add(monthDto);
                    }
                    continue;
                }

                // 2. 迭代该月内的每一天进行比对
                int daysInMonth = DateTime.DaysInMonth(monthDate.Year, monthDate.Month);
                for (int d = 1; d <= daysInMonth; d++)
                {
                    var currentDay = new DateTime(monthDate.Year, monthDate.Month, d);
                    ct.ThrowIfCancellationRequested();

                    // 过滤不在查询范围内的日期
                    if (currentDay.Date < startDate.Date || currentDay.Date > endDate.Date) continue;

                    // 路径模板可能包含 {dd}/{d}，必须用当天日期解析目录。
                    string actualFolderPath = FileDateTimeUtil.GetDateTimeFromBrace(config.FilePathPattern, currentDay);
                    if (string.IsNullOrEmpty(monthDto.FolderPath))
                    {
                        monthDto.FolderPath = actualFolderPath;
                    }

                    string expectedFileName = FileDateTimeUtil.GetProcessedFileName(config, currentDay);
                    string expectedName = isFolderMode
                        ? BuildFolderModeExpectedName(extension)
                        : NormalizeExpectedFileName(expectedFileName, extension);

                    if (!isFolderMode)
                    {
                        string expectedFullPath = CombinePath(actualFolderPath, expectedName);
                        try
                        {
                            var provider = _factory.Create(actualFolderPath, credentials.UserName, credentials.Password);
                            if (provider.Exists(expectedFullPath))
                            {
                                var entry = new FileEntryDto
                                {
                                    FileName = expectedName,
                                    FullPath = expectedFullPath,
                                    DetectedDate = currentDay,
                                    IsMissing = false
                                };

                                await AttachFileMetadataAsync(entry, provider, ct).ConfigureAwait(false);
                                AttachFileState(entry, fileStateMap);
                                monthDto.Files.Add(entry);
                                continue;
                            }
                        }
                        catch
                        {
                            // 精确判断失败后继续走目录扫描兜底。
                        }
                    }

                    string folderCacheKey = actualFolderPath ?? string.Empty;
                    if (!folderFilesCache.TryGetValue(folderCacheKey, out IEnumerable<string> existingFiles))
                    {
                        try
                        {
                            var provider = _factory.Create(actualFolderPath, credentials.UserName, credentials.Password);
                            var scanOptions = FolderScanOptionsUtil.FromConfig(config);
                            existingFiles = await FolderScanOptionsUtil.GetFilesAsync(provider, actualFolderPath, "*", scanOptions, ct)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Discovery][Warning] Scan folder failed. ConfigId={config.Id}, EqName={config.EqName}, Folder={actualFolderPath}, Error={ex.Message}");
                            existingFiles = Enumerable.Empty<string>();
                        }

                        folderFilesCache[folderCacheKey] = existingFiles;
                    }

                    // 3. 在文件清单中查找匹配项：文件夹模式只校验类型；普通模式校验完整文件名
                    var matches = existingFiles
                        .Where(f => {
                            string actualFileName = Path.GetFileName(f);
                            if (!HasExpectedExtension(actualFileName, extension)) return false;

                            return isFolderMode ||
                                actualFileName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();

                    if (!matches.Any() && !isFolderMode)
                    {
                        string expectedFullPath = CombinePath(actualFolderPath, expectedName);
                        try
                        {
                            var provider = _factory.Create(actualFolderPath, credentials.UserName, credentials.Password);
                            if (provider.Exists(expectedFullPath))
                            {
                                matches.Add(expectedFullPath);
                            }
                        }
                        catch
                        {
                            // 保持原有缺失判断。
                        }
                    }

                    if (matches.Any())
                    {
                        // 发现文件：添加到 DTO
                        foreach (var fileName in matches)
                        {
                            string actualFileName = Path.GetFileName(fileName);
                            string fullPath = BuildDetectedFilePath(actualFolderPath, fileName);
                            var entry = new FileEntryDto
                            {
                                FileName = actualFileName,
                                FullPath = fullPath,
                                DetectedDate = currentDay,
                                IsMissing = false
                            };

                            var provider = _factory.Create(actualFolderPath, credentials.UserName, credentials.Password);
                            await AttachFileMetadataAsync(entry, provider, ct).ConfigureAwait(false);
                            AttachFileState(entry, fileStateMap);
                            monthDto.Files.Add(entry);
                        }
                    }
                    else
                    {
                        // 未发现文件：标记缺失项
                        var entry = new FileEntryDto
                        {
                            FileName = expectedName, // 展示预期的文件名或文件夹模式通配规则
                            FullPath = isFolderMode ? actualFolderPath : CombinePath(actualFolderPath, expectedName),
                            DetectedDate = currentDay,
                            IsMissing = true
                        };

                        AttachFileState(entry, fileStateMap);
                        monthDto.Files.Add(entry);
                    }
                }

                // 只有当该月有在范围内的日期时，才加入结果集
                if (monthDto.Files.Any())
                {
                    resultList.Add(monthDto);
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"[Discovery] ConfigId={config.Id}, EqName={config.EqName}, Range={startDate:yyyy-MM-dd}~{endDate:yyyy-MM-dd}, Mode={discoveryMode}, Files={resultList.Sum(x => x.Files?.Count ?? 0)}, ElapsedMs={stopwatch.ElapsedMilliseconds}");

            return resultList;
        }

        private async Task AppendMonthlyFolderCalendarFilesAsync(
            AcquisitionConfig config,
            FileDiscoveryDto monthDto,
            DateTime monthDate,
            DateTime startDate,
            DateTime endDate,
            string extension,
            FileAccessCredentials credentials,
            Dictionary<string, AcquisitionFileState> fileStateMap,
            CancellationToken ct)
        {
            string actualFolderPath = FileDateTimeUtil.GetDateTimeFromBrace(config.FilePathPattern, monthDate);
            monthDto.FolderPath = actualFolderPath;

            var provider = _factory.Create(actualFolderPath, credentials.UserName, credentials.Password);
            var scanOptions = FolderScanOptionsUtil.FromConfig(config);
            IEnumerable<string> existingFiles;

            try
            {
                existingFiles = await FolderScanOptionsUtil.GetFilesAsync(provider, actualFolderPath, "*", scanOptions, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery][Warning] Scan monthly folder failed. ConfigId={config.Id}, EqName={config.EqName}, Folder={actualFolderPath}, Error={ex.Message}");
                existingFiles = Enumerable.Empty<string>();
            }

            var entries = existingFiles
                .Where(f => HasExpectedExtension(Path.GetFileName(f), extension))
                .Select(file =>
                {
                    string fullPath = BuildDetectedFilePath(actualFolderPath, file);
                    string actualFileName = Path.GetFileName(file);
                    return new FileEntryDto
                    {
                        FileName = actualFileName,
                        FullPath = fullPath,
                        DetectedDate = monthDate.Date,
                        IsMissing = false
                    };
                })
                .ToList();

            await AttachFileMetadataAsync(entries, provider, ct).ConfigureAwait(false);

            var filesByDate = new Dictionary<DateTime, List<FileEntryDto>>();
            foreach (var entry in entries)
            {
                DateTime fileDate = (entry.LastWriteTime ?? monthDate).Date;
                if (fileDate < startDate.Date || fileDate > endDate.Date)
                {
                    continue;
                }

                entry.DetectedDate = fileDate;
                AttachFileState(entry, fileStateMap);

                if (!filesByDate.TryGetValue(fileDate, out var dayFiles))
                {
                    dayFiles = new List<FileEntryDto>();
                    filesByDate[fileDate] = dayFiles;
                }

                dayFiles.Add(entry);
            }

            int daysInMonth = DateTime.DaysInMonth(monthDate.Year, monthDate.Month);
            for (int d = 1; d <= daysInMonth; d++)
            {
                ct.ThrowIfCancellationRequested();
                var currentDay = new DateTime(monthDate.Year, monthDate.Month, d);
                if (currentDay.Date < startDate.Date || currentDay.Date > endDate.Date) continue;

                if (filesByDate.TryGetValue(currentDay.Date, out var dayFiles) && dayFiles.Any())
                {
                    monthDto.Files.AddRange(dayFiles
                        .OrderByDescending(x => x.LastWriteTime ?? DateTime.MinValue)
                        .ThenBy(x => x.FileName));
                    continue;
                }

                var missing = new FileEntryDto
                {
                    FileName = BuildFolderModeExpectedName(extension),
                    FullPath = actualFolderPath,
                    DetectedDate = currentDay,
                    IsMissing = true
                };

                AttachFileState(missing, fileStateMap);
                monthDto.Files.Add(missing);
            }
        }

        private bool HasDayGranularity(string pattern)
        {
            return !string.IsNullOrWhiteSpace(pattern) &&
                (pattern.IndexOf("{dd}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 pattern.IndexOf("{d}", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool HasMonthGranularity(string pattern)
        {
            return !string.IsNullOrWhiteSpace(pattern) &&
                (pattern.IndexOf("{MM}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 pattern.IndexOf("{M}", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private async Task AttachFileMetadataAsync(FileEntryDto entry, IFileProvider provider, CancellationToken ct)
        {
            if (entry == null || provider == null || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return;
            }

            try
            {
                FileMetadata metadata = await provider.GetFileMetadataAsync(entry.FullPath, ct).ConfigureAwait(false);
                entry.LastWriteTime = metadata?.LastWriteTime;
                entry.FileSize = metadata?.Length;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 元数据失败不影响巡检结果。
            }
        }

        private async Task AttachFileMetadataAsync(List<FileEntryDto> entries, IFileProvider provider, CancellationToken ct)
        {
            if (entries == null || entries.Count == 0 || provider == null)
            {
                return;
            }

            using (var semaphore = new SemaphoreSlim(MetadataConcurrency))
            {
                var tasks = entries.Select(async entry =>
                {
                    await semaphore.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await AttachFileMetadataAsync(entry, provider, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 私有工具：安全拼接路径，确保兼容 FTP (正斜杠)
        /// </summary>
        private string CombinePath(string folder, string file)
        {
            if (string.IsNullOrEmpty(folder)) return file;
            if (string.IsNullOrEmpty(file)) return folder;

            string trimmedFolder = folder.TrimEnd('/', '\\');
            string trimmedFile = file.TrimStart('/', '\\');
            if (IsUrlPath(trimmedFolder))
            {
                return trimmedFolder + "/" + trimmedFile.Replace('\\', '/');
            }

            return trimmedFolder + "\\" + trimmedFile.Replace('/', '\\');
        }

        private string BuildDetectedFilePath(string folder, string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return file;
            if (file.Contains("://") || Path.IsPathRooted(file)) return file;

            return CombinePath(folder, file);
        }

        private bool IsUrlPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                (path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private FileAccessCredentials ResolveCredentials(AcquisitionConfig config, string user, string pass)
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

            if (access.HasUserName)
            {
                return new FileAccessCredentials
                {
                    UserName = access.EffectiveUserName,
                    Password = access.HasPassword ? access.GetPassword() : null
                };
            }

            if (!string.IsNullOrWhiteSpace(user))
            {
                return new FileAccessCredentials { UserName = user, Password = pass };
            }

            return new FileAccessCredentials
            {
                UserName = null,
                Password = null
            };
        }

        private class FileAccessCredentials
        {
            public string UserName { get; set; }
            public string Password { get; set; }
        }

        private string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return string.Empty;

            extension = extension.Trim();
            return extension.StartsWith(".") ? extension : "." + extension;
        }

        private string NormalizeExpectedFileName(string expectedFileName, string extension)
        {
            string expectedName = Path.GetFileName((expectedFileName ?? string.Empty).Replace("*", ""));

            if (!string.IsNullOrEmpty(extension) &&
                !expectedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                expectedName += extension;
            }

            return expectedName;
        }

        private string BuildFolderModeExpectedName(string extension)
        {
            return string.IsNullOrEmpty(extension) ? "*.*" : "*" + extension;
        }

        private bool HasExpectedExtension(string fileName, string extension)
        {
            return string.IsNullOrEmpty(extension) ||
                fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        private void AttachFileState(FileEntryDto entry, Dictionary<string, AcquisitionFileState> fileStateMap)
        {
            if (entry == null || fileStateMap == null)
            {
                return;
            }

            if (!fileStateMap.TryGetValue(BuildStateKey(entry.DetectedDate, entry.FileName), out var state))
            {
                return;
            }

            entry.DataRowCount = state.DataRowCount;
            entry.LastStartRow = state.LastStartRow;
            entry.LastProcessedRows = state.LastProcessedRows;
            entry.LastStatus = state.LastStatus;
            entry.LastUpdateSource = state.LastUpdateSource;
            entry.IsSealed = state.IsSealed;
            entry.LastScanTime = state.LastScanTime;
            entry.FileStateUpdateTime = state.UpdateTime;
        }

        private string BuildStateKey(DateTime businessDate, string fileName)
        {
            return $"{businessDate:yyyyMMdd}|{(fileName ?? string.Empty).Trim().ToUpperInvariant()}";
        }
    }
}

