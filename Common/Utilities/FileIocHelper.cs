using DT_DataAcquisitionSystem.Domain.Interfaces;
using Microsoft.Practices.Unity;
using Microsoft.Practices.Unity.Configuration;
using System.Configuration;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    /// <summary>
    /// FileContainer 专用 IOC 帮助类
    /// </summary>
    public static class FileIocHelper
    {
        private static readonly IUnityContainer _container;

        static FileIocHelper()
        {
            _container = new UnityContainer();
            var section = (UnityConfigurationSection)ConfigurationManager.GetSection("unity");
            section.Configure(_container, "FileContainer");   // 关键：只加载 FileContainer
        }

        /// <summary>
        /// 获取文件工厂（默认注册，无 name）
        /// </summary>
        public static IFileProviderFactory GetFileProviderFactory()
        {
            return _container.Resolve<IFileProviderFactory>();
        }

        /// <summary>
        /// 如果工厂需要构造函数参数
        /// </summary>
        public static IFileProviderFactory GetFileProviderFactory(params ParameterOverride[] parameters)
        {
            return parameters == null || parameters.Length == 0
                ? _container.Resolve<IFileProviderFactory>()
                : _container.Resolve<IFileProviderFactory>(parameters);
        }

        /// <summary>
        /// 获取具体文件提供者（local / ftp）
        /// </summary>
        /// <param name="name">local 或 ftp</param>
        public static IFileProvider GetFileProvider(string name)
        {
            return _container.Resolve<IFileProvider>(name);
        }

        /// <summary>
        /// 带参数的命名解析（如果以后需要）
        /// </summary>
        public static IFileProvider GetFileProvider(string name, params ParameterOverride[] parameters)
        {
            return parameters == null || parameters.Length == 0
                ? _container.Resolve<IFileProvider>(name)
                : _container.Resolve<IFileProvider>(name, parameters);
        }
    }
}