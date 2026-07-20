using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;

namespace DT_DataAcquisitionSystem.Application.Services
{
    public interface ITestAcquisitionService
    {
        Task<TestAcquisitionRunDto> StartAsync(TestAcquisitionStartRequestDto request, CancellationToken ct = default);

        Task<TestAcquisitionRunDto> SelectSourceAsync(string testRunId, TestAcquisitionSelectSourceRequestDto request, CancellationToken ct = default);

        Task<TestAcquisitionRunDto> GetAsync(string testRunId, CancellationToken ct = default);

        Task<TestAcquisitionCleanupResultDto> CleanupAsync(string testRunId, CancellationToken ct = default);
    }
}
