using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface IReportExportService
    {
        Task<ReportExportCreateResponseDto> CreateTaskAsync(ReportExportCreateRequestDto request);
        ReportExportTaskDto GetTask(string id);
        string GetDownloadFilePath(string id, out string fileName);
        void ExecuteTask(string id);
    }
}
