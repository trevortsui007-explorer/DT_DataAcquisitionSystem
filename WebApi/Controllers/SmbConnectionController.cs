using System;
using DT_DataAcquisitionSystem.Common.Extensions;
using DT_DataAcquisitionSystem.Common.Utilities;
using Learun.Application.WebApi;
using Nancy;
using Nancy.ModelBinding;

namespace DT_DataAcquisitionSystem.WebApi.Controllers
{
    public class SmbConnectionController : BaseApi
    {
        public SmbConnectionController() : base("/api/data-acquisition/smb-connections")
        {
            Get["/"] = GetConnections;
            Post["/disconnect-server"] = DisconnectServer;
            Post["/disconnect-all"] = DisconnectAll;
        }

        private Response GetConnections(dynamic _)
        {
            return this.ToResponse(
                NancyModuleExtensions.ResponseCode.success,
                "查询成功",
                SmbConnectionScope.GetSnapshots());
        }

        private Response DisconnectServer(dynamic _)
        {
            var request = this.Bind<DisconnectServerRequest>();

            if (request == null || string.IsNullOrWhiteSpace(request.Server))
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.fail,
                    "server 不能为空",
                    null);
            }

            var errors = SmbConnectionScope.DisconnectServer(
                request.Server,
                request.Force);

            return BuildDisconnectResponse(errors);
        }

        private Response DisconnectAll(dynamic _)
        {
            var request = this.Bind<DisconnectAllRequest>() ?? new DisconnectAllRequest();
            var errors = SmbConnectionScope.DisconnectAll(request.Force);

            return BuildDisconnectResponse(errors);
        }

        private Response BuildDisconnectResponse(System.Collections.Generic.IReadOnlyList<string> errors)
        {
            var data = new
            {
                Errors = errors,
                Connections = SmbConnectionScope.GetSnapshots()
            };

            if (errors.Count == 0)
            {
                return this.ToResponse(
                    NancyModuleExtensions.ResponseCode.success,
                    "断开成功",
                    data);
            }

            return this.ToResponse(
                NancyModuleExtensions.ResponseCode.fail,
                string.Join(Environment.NewLine, errors),
                data);
        }

        private sealed class DisconnectServerRequest
        {
            public string Server { get; set; }

            public bool Force { get; set; }
        }

        private sealed class DisconnectAllRequest
        {
            public bool Force { get; set; }
        }
    }
}
