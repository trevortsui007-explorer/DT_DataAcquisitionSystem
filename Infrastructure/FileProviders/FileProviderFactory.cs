using System;
using System.Collections.Generic;
using System.Linq;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Infrastructure
{
    public class FileProviderFactory : IFileProviderFactory
    {
        private readonly IFileProvider[] _providers;

        public FileProviderFactory(IFileProvider[] providers)
        {
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        }

        public IFileProvider Create(string path, string username = null, string password = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("路径不能为空", nameof(path));

            // 1. 查找能处理该路径的 Provider
            var provider = _providers.FirstOrDefault(p => p.CanHandle(path));

            if (provider == null)
            {
                throw new NotSupportedException($"未找到支持该路径协议的 FileProvider: {path}");
            }

            // 2. 动态注入凭证（只对实现了 ICredentialSupported 的 Provider 生效）
            if (provider is ICredentialSupported credentialProvider && !string.IsNullOrEmpty(username))
            {
                credentialProvider.SetCredentials(username, password);
            }

            return provider;
        }
    }
}