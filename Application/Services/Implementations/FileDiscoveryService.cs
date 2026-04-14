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

        public FileDiscoveryService()
        {
            _factory = FileIocHelper.GetFileProviderFactory();
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

            // 1. 按月循环，优化扫描效率
            for (var monthDate = new DateTime(startDate.Year, startDate.Month, 1); 
                 monthDate <= endDate; 
                 monthDate = monthDate.AddMonths(1))
            {
                // 解析当月文件夹物理路径
                string actualFolderPath = FileDateTimeUtil.GetDateTimeFromBrace(config.FilePathPattern, monthDate);
                
                var monthDto = new FileDiscoveryDto
                {
                    MonthName = monthDate.ToString("yyyy-MM"),
                    FolderPath = actualFolderPath,
                    Files = new List<FileEntryDto>()
                };

                IEnumerable<string> existingFiles = new List<string>();
                bool folderExists = true;

                try
                {
                    // 获取 Provider 并拉取全量文件名清单 (避免在日循环里反复请求网络)
                    var provider = _factory.Create(actualFolderPath, user, pass);
                    existingFiles = await provider.GetFileNamesAsync(actualFolderPath, "*");
                }
                catch
                {
                    // 如果文件夹不存在或无法访问，标记该月所有文件为缺失
                    folderExists = false;
                }

                // 2. 迭代该月内的每一天进行比对
                int daysInMonth = DateTime.DaysInMonth(monthDate.Year, monthDate.Month);
                for (int d = 1; d <= daysInMonth; d++)
                {
                    var currentDay = new DateTime(monthDate.Year, monthDate.Month, d);

                    // 过滤不在查询范围内的日期
                    if (currentDay.Date < startDate.Date || currentDay.Date > endDate.Date) continue;

                    // 根据模板生成预期的文件名关键特征
                    string expectedFileName = FileDateTimeUtil.GetProcessedFileName(config, currentDay);
                    string searchKey = expectedFileName.Replace("*", "");

                    // 获取配置中的后缀 (假设属性名为 config.FileExtension，如 ".csv")
                    string extension = config.FileType ?? "";

                    // 3. 在文件清单中查找匹配项：包含关键字 且 以指定后缀结尾
                    var matches = existingFiles
                        .Where(f => {
                            // 1. 先校验后缀
                            if (!f.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return false;

                            // 2. 核心：确保文件名去掉后缀后，与预期文件名完全一致
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(f);
                            string expectedWithoutStar = expectedFileName.Replace("*", "");

                            return fileNameWithoutExt.Equals(expectedWithoutStar, StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();

                    if (matches.Any())
                    {
                        // 发现文件：添加到 DTO
                        foreach (var fileName in matches)
                        {
                            monthDto.Files.Add(new FileEntryDto
                            {
                                FileName = fileName,
                                FullPath = CombinePath(actualFolderPath, fileName),
                                DetectedDate = currentDay,
                                IsMissing = false
                            });
                        }
                    }
                    else
                    {
                        // 未发现文件：标记缺失项
                        monthDto.Files.Add(new FileEntryDto
                        {
                            FileName = expectedFileName, // 展示预期的文件名
                            FullPath = CombinePath(actualFolderPath, expectedFileName),
                            DetectedDate = currentDay,
                            IsMissing = true
                        });
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
    }
}