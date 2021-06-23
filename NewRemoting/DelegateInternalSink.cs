using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;

namespace NewRemoting
{
	/// <summary>
	/// This class acts as a server-side sink for delegates.
	/// An instance of this class is a registration to a single event
	/// </summary>
	internal class DelegateInternalSink
	{
		private readonly IInterceptor _interceptor;
		private readonly string _remoteObjectReference;
		private readonly MethodInfo _remoteMethodTarget;

		public DelegateInternalSink(IInterceptor interceptor, string remoteObjectReference, MethodInfo remoteMethodTarget)
		{
			_interceptor = interceptor;
			_remoteObjectReference = remoteObjectReference;
			_remoteMethodTarget = remoteMethodTarget;
		}

		public void ActionSink()
		{
			DoCallback();
		}

		public void ActionSink<T>(T arg1)
		{
			DoCallback(arg1);
		}

		public void ActionSink<T1, T2>(T1 arg1, T2 arg2)
		{
			DoCallback(arg1, arg2);
		}

		public T FuncSink<T>()
		{
			return DoCallback<T>();
		}

		public TRet FuncSink<T1, TRet>(T1 arg1)
		{
			return DoCallback<TRet>(arg1);
		}

		public TRet FuncSink<T1, T2, TRet>(T1 arg1, T2 arg2)
		{
			return DoCallback<TRet>(arg1, arg2);
		}

		private void DoCallback(params object[] args)
		{
			ManualInvocation ri = new ManualInvocation(_remoteMethodTarget, args);
			ri.Proxy = this; // Works, because this instance is registered as proxy

			_interceptor.Intercept(ri);
		}

		private T DoCallback<T>(params object[] args)
		{
			ManualInvocation ri = new ManualInvocation(_remoteMethodTarget, args);
			ri.Proxy = this; // Works, because this instance is registered as proxy

			_interceptor.Intercept(ri);
			return (T)ri.ReturnValue;
		}

	}
}
