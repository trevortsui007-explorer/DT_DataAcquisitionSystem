using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Domain.Entities;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public class AcquisitionLogService : IAcquisitionLogService
    {
        private readonly IFileConfigRepository _configRepo;
        private readonly IAcquisitionLogRepository _logRepo;

        public AcquisitionLogService(IFileConfigRepository configRepo, IAcquisitionLogRepository logRepo)
        {
            _configRepo = configRepo;
            _logRepo = logRepo;
        }

        /// <summary>
        /// 获取下一次采集的起始行号：优先找日志断点，其次找初始配置
        /// </summary>
        public async Task<int> GetNextStartRowAsync(int configId, string fileName, CancellationToken ct = default)
        {
            // 1. 尝试从日志表获取断点（异步调用）
            int lastPos = await _logRepo.GetLastProcessedRowByConfigIdAsync(configId, fileName, ct);
            if (lastPos > 0) return lastPos;

            // 2. 如果没有日志，从配置表获取初始位置
            var ids = new List<string> { configId.ToString() };
            var configList = _configRepo.GetListByIds(ids);
            var config = configList?.FirstOrDefault();

            // 3. 返回配置的起始行，如果没有配置则返回 0
            return config?.StartRow ?? 0;
        }

        /// <summary>
        /// 记录本次采集的详细进度到 DA_AcquisitionLog
        /// </summary>
        public async Task<string> RecordLogEntryAsync(AcquisitionLogEntry entry, CancellationToken ct = default)
        {
            // 1. 防御性校验
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            if (entry.ConfigId <= 0 || string.IsNullOrWhiteSpace(entry.TaskLogId))
            {
                throw new InvalidOperationException("记录明细日志失败：ConfigId 必须大于 0，且 TaskLogId 不能为空。");
            }

            // 2. 状态归一化：防止大小写或空格导致后续统计困难
            if (!string.IsNullOrWhiteSpace(entry.Status))
            {
                entry.Status = entry.Status.Trim();
            }

            // 3. 执行入库
            try
            {
                return await _logRepo.InsertAsync(entry, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 这里可以增加重试逻辑，或者写入本地备用日志
                // Log.Error("持久化采集明细进度失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 记录采集任务总日志到 DA_AcquisitionTaskLog
        /// </summary>
        public async Task<string> RecordTaskLogEntryAsync(AcquisitionTaskLogEntry entry, CancellationToken ct = default)
        {
            // 1. 防御性校验
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            // 0 为手动采集，1...N 为设置的任务
            if (entry.TaskId < 0)
            {
                throw new InvalidOperationException("记录任务总日志失败：TaskId 必须 >= 0。");
            }

            // 2. 状态归一化
            if (!string.IsNullOrWhiteSpace(entry.Status))
            {
                entry.Status = entry.Status.Trim();
            }

            // 3. 执行入库
            try
            {
                return await _logRepo.InsertAsync(entry, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 这里可以增加重试逻辑，或者写入本地备用日志
                // Log.Error("持久化任务总日志失败", ex);
                throw;
            }
        }


        /// <summary>
        /// 更新采集任务总日志到 DA_AcquisitionTaskLog
        /// </summary>
        public async Task<bool> UpdateTaskStatusAsync(string id, string status, int successCount, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var entry = new AcquisitionTaskLogEntry
            {
                Id = id,
                Status = status?.Trim(),
                SuccessCount = successCount,
                EndTime = DateTime.Now // 假设更新状态时即为结束或阶段性记录
            };

            try
            {
                return await _logRepo.UpdateAsync(entry, ct);
            }
            catch (Exception ex)
            {
                // Log.Error("更新任务状态失败", ex);
                throw;
            }
        }
    }
}