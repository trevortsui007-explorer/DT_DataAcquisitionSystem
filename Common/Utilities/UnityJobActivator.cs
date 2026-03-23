using System;
using Hangfire;
using Microsoft.Practices.Unity;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    /// <summary>
    /// 手动实现的 Hangfire 激活器，解决 Unity 版本冲突
    /// </summary>
    public class UnityJobActivator : JobActivator
    {
        private readonly IUnityContainer _container;

        public UnityJobActivator(IUnityContainer container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public override object ActivateJob(Type type)
        {
            // 使用你项目现有的 Unity 接口解析对象
            return _container.Resolve(type);
        }
    }
}