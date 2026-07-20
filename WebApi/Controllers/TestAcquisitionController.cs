using System;
using System.Threading;
using System.Threading.Tasks;
using DT_DataAcquisitionSystem.Application.DTOs;
using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Common.Extensions;
using Learun.Application.WebApi;
using Nancy;
using Nancy.ModelBinding;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    public class TestAcquisitionController : BaseApi
    {
        private readonly ITestAcquisitionService _testAcquisitionService;

        public TestAcquisitionController(ITestAcquisitionService testAcquisitionService)
            : base("/api/data-acquisition/test-acquisition")
        {
            _testAcquisitionService = testAcquisitionService;

            Post["/start", true] = async (p, ct) => await Start(p, ct);
            Get["/{testRunId}", true] = async (p, ct) => await GetRun(p, ct);
            Post["/{testRunId}/select-source", true] = async (p, ct) => await SelectSource(p, ct);
            Post["/{testRunId}/cleanup", true] = async (p, ct) => await Cleanup(p, ct);
        }

        private async Task<Response> Start(dynamic p, CancellationToken ct)
        {
            try
            {
                var request = this.Bind<TestAcquisitionStartRequestDto>() ?? new TestAcquisitionStartRequestDto();
                var result = await _testAcquisitionService.StartAsync(request, ct).ConfigureAwait(false);
                bool success = result != null && !string.Equals(result.Status, "Failed", StringComparison.OrdinalIgnoreCase);

                return this.ToResponse(
                    success ? NancyModuleExtensions.ResponseCode.success : NancyModuleExtensions.ResponseCode.fail,
                    result?.Message ?? "测试采集失败",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "测试采集启动异常: " + ex.Message,
                    null);
            }
        }

        private async Task<Response> SelectSource(dynamic p, CancellationToken ct)
        {
            string testRunId = (string)p.testRunId;
            if (string.IsNullOrWhiteSpace(testRunId))
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "鍙傛暟閿欒锛歵estRunId 涓嶈兘涓虹┖", null);
            }

            try
            {
                var request = this.Bind<TestAcquisitionSelectSourceRequestDto>() ?? new TestAcquisitionSelectSourceRequestDto();
                var result = await _testAcquisitionService.SelectSourceAsync(testRunId, request, ct).ConfigureAwait(false);
                bool success = result != null && !string.Equals(result.Status, "Failed", StringComparison.OrdinalIgnoreCase);

                return this.ToResponse(
                    success ? NancyModuleExtensions.ResponseCode.success : NancyModuleExtensions.ResponseCode.fail,
                    result?.Message ?? "Select source failed",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "Select source exception: " + ex.Message, null);
            }
        }

        private async Task<Response> GetRun(dynamic p, CancellationToken ct)
        {
            string testRunId = (string)p.testRunId;
            if (string.IsNullOrWhiteSpace(testRunId))
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：testRunId 不能为空", null);
            }

            try
            {
                var result = await _testAcquisitionService.GetAsync(testRunId, ct).ConfigureAwait(false);
                if (result == null)
                {
                    return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "未找到测试采集记录", null);
                }

                return this.ToResponse(NancyModuleExtensions.ResponseCode.success, "获取测试采集记录成功", result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "获取测试采集记录异常: " + ex.Message, null);
            }
        }

        private async Task<Response> Cleanup(dynamic p, CancellationToken ct)
        {
            string testRunId = (string)p.testRunId;
            if (string.IsNullOrWhiteSpace(testRunId))
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "参数错误：testRunId 不能为空", null);
            }

            try
            {
                var result = await _testAcquisitionService.CleanupAsync(testRunId, ct).ConfigureAwait(false);
                return this.ToResponse(
                    result != null && result.Success ? NancyModuleExtensions.ResponseCode.success : NancyModuleExtensions.ResponseCode.fail,
                    result?.Message ?? "清理测试数据失败",
                    result);
            }
            catch (Exception ex)
            {
                return this.ToResponse(NancyModuleExtensions.ResponseCode.fail, "清理测试数据异常: " + ex.Message, null);
            }
        }
    }
}
