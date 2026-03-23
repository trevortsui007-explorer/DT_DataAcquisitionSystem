using System.Collections.Generic;
using System.Configuration;
using Microsoft.Practices.Unity;
using Microsoft.Practices.Unity.Configuration;
using DT_DataAcquisitionSystem.Application.Services;
using DT_DataAcquisitionSystem.Domain.Interfaces;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    /// <summary>
    /// ProcessorContainer 专用 IOC 帮助类（工厂模式）
    /// </summary>
    public static class ProcessorIocHelper
    {
        private static readonly IUnityContainer _container;

        static ProcessorIocHelper()
        {
            _container = new UnityContainer();
            var section = (UnityConfigurationSection)ConfigurationManager.GetSection("unity");

            if (section != null)
            {
                // 加载 XML 中定义的 "ProcessorContainer" 里的插件配置
                section.Configure(_container, "ProcessorContainer");
            }
        }

        /// <summary>
        /// 工厂方法：创建一个完整的 PostProcessingService
        /// </summary>
        /// <param name="dataService">由外部（主容器）提供的 DataService 实例</param>
        public static IPostProcessingService CreatePostProcessingService(IDataService dataService)
        {
            // 核心逻辑：将主容器已经注入好的 dataService 实例注册进这个小容器
            // 这样小容器在解析 MasonETFailureProcessor 时，就知道从哪拿 IDataService 了
            if (!_container.IsRegistered<IDataService>())
            {
                _container.RegisterInstance<IDataService>(dataService);
            }

            // 1. 获取所有在 XML 注册的 IPostProcessor (比如 MasonET)
            var processors = _container.ResolveAll<IPostProcessor>();

            // 2. 手动创建服务主体并返回
            // 这样做就不需要在 XML 里配置 IPostProcessingService 的别名和映射了
            return new PostProcessingService(dataService, processors);
        }

        /// <summary>
        /// 如果以后需要单独获取某个 Processor 也可以用这个
        /// </summary>
        public static IPostProcessor GetProcessor(string name)
        {
            return _container.Resolve<IPostProcessor>(name);
        }
    }
}