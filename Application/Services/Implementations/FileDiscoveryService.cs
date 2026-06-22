using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Common.Utilities;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class FileDiscoveryService
    {
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
            string pass = "")
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            
            var resultList = new List<FileDiscoveryDto>();
            var fileStates = await _fileStateService
                .GetByConfigAndDateRangeAsync(config.Id, startDate, endDate)
                .ConfigureAwait(false);
            var fileStateMap = fileStates
                .GroupBy(x => BuildStateKey(x.BusinessDate, x.FileName))
                .ToDictionary(x => x.Key, x => x.OrderByDescending(s => s.UpdateTime).First());

            var folderFilesCache = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);

            // 1. 按月组织返回结构，按实际解析出的目录路径缓存扫描结果
            for (var monthDate = new DateTime(startDate.Year, startDate.Month, 1); 
                 monthDate <= endDate; 
                 monthDate = monthDate.AddMonths(1))
            {
                var monthDto = new FileDiscoveryDto
                {
                    MonthName = monthDate.ToString("yyyy-MM"),
                    FolderPath = string.Empty,
                    Files = new List<FileEntryDto>()
                };

                // 2. 迭代该月内的每一天进行比对
                int daysInMonth = DateTime.DaysInMonth(monthDate.Year, monthDate.Month);
                for (int d = 1; d <= daysInMonth; d++)
                {
                    var currentDay = new DateTime(monthDate.Year, monthDate.Month, d);

                    // 过滤不在查询范围内的日期
                    if (currentDay.Date < startDate.Date || currentDay.Date > endDate.Date) continue;

                    // 路径模板可能包含 {dd}/{d}，必须用当天日期解析目录。
                    string actualFolderPath = FileDateTimeUtil.GetDateTimeFromBrace(config.FilePathPattern, currentDay);
                    if (string.IsNullOrEmpty(monthDto.FolderPath))
                    {
                        monthDto.FolderPath = actualFolderPath;
                    }

                    string folderCacheKey = actualFolderPath ?? string.Empty;
                    if (!folderFilesCache.TryGetValue(folderCacheKey, out IEnumerable<string> existingFiles))
                    {
                        try
                        {
                            var provider = _factory.Create(actualFolderPath, user, pass);
                            existingFiles = await provider.GetFileNamesAsync(actualFolderPath, "*");
                        }
                        catch
                        {
                            // 如果文件夹不存在或无法访问，标记当天文件为缺失。
                            existingFiles = Enumerable.Empty<string>();
                        }

                        folderFilesCache[folderCacheKey] = existingFiles;
                    }

                    // 根据模板生成预期的文件名关键特征
                    string expectedFileName = FileDateTimeUtil.GetProcessedFileName(config, currentDay);
                    bool isFolderMode = string.IsNullOrWhiteSpace(config.FileNamePattern);

                    // 获取配置中的后缀 (假设属性名为 config.FileExtension，如 ".csv")
                    string extension = NormalizeExtension(config.FileType);
                    string expectedName = isFolderMode
                        ? BuildFolderModeExpectedName(extension)
                        : NormalizeExpectedFileName(expectedFileName, extension);

                    // 3. 在文件清单中查找匹配项：文件夹模式只校验类型；普通模式校验完整文件名
                    var matches = existingFiles
                        .Where(f => {
                            string actualFileName = Path.GetFileName(f);
                            if (!HasExpectedExtension(actualFileName, extension)) return false;

                            return isFolderMode ||
                                actualFileName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();

                    if (matches.Any())
                    {
                        // 发现文件：添加到 DTO
                        foreach (var fileName in matches)
                        {
                            string actualFileName = Path.GetFileName(fileName);
                            var entry = new FileEntryDto
                            {
                                FileName = actualFileName,
                                FullPath = CombinePath(actualFolderPath, actualFileName),
                                DetectedDate = currentDay,
                                IsMissing = false
                            };

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
                            FullPath = CombinePath(actualFolderPath, expectedName),
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

            return resultList;
        }

        /// <summary>
        /// 私有工具：安全拼接路径，确保兼容 FTP (正斜杠)
        /// </summary>
        private string CombinePath(string folder, string file)
        {
            if (string.IsNullOrEmpty(folder)) return file;
            return folder.TrimEnd('/', '\\') + "/" + file.TrimStart('/', '\\');
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

