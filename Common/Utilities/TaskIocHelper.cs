using DT_DataAcquisitionSystem.Domain.Interfaces;
using Microsoft.Practices.Unity;
using Microsoft.Practices.Unity.Configuration;
using System;
using System.Configuration;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    /// <summary>
    /// TaskContainer 专用 IOC 帮助类
    /// </summary>
    public static class TaskIocHelper
    {
        private static readonly IUnityContainer _container;

        static TaskIocHelper()
        {
            _container = new UnityContainer();
            var section = (UnityConfigurationSection)ConfigurationManager.GetSection("unity");

            if (section != null)
            {
                section.Configure(_container, "TaskContainer");
            }
        }

        public static IUnityContainer Container => _container;

        public static IAcquisitionTaskService GetTaskService()
            => _container.Resolve<IAcquisitionTaskService>();
    }
}