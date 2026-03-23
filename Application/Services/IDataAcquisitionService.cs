using DT_DataAcquisitionSystem.Application.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IDataAcquisitionService
    {
        Task<AcquisitionSummary> ProcessByTask(string[] taskids, DateTime processdate, CancellationToken ct = default);
    }
}