using System;
using System.Collections.Generic;

namespace NewRemoting
{
	/// <summary>
	/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
	/// </summary>
	internal class DelegateProxyOnClient : DelegateProxyOnClientBase
	{
		public DelegateProxyOnClient()
		{
		}

		public event Action Event;

		protected override bool IsEmpty => Event == null;

		public void FireEvent()
		{
			Event?.Invoke();
		}
	}

	/// <summary>
	/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
	/// </summary>
	internal class DelegateProxyOnClient<T> : DelegateProxyOnClientBase
	{
		public DelegateProxyOnClient()
		{
		}

		public event Action<T> Event;

		protected override bool IsEmpty => Event == null;

		public void FireEvent(T arg)
		{
			Event?.Invoke(arg);
		}
	}

	internal class DelegateProxyOnClient<T1, T2> : DelegateProxyOnClientBase
	{
		public DelegateProxyOnClient()
		{
		}

		public event Action<T1, T2> Event;

		protected override bool IsEmpty => Event == null;

		public void FireEvent(T1 arg1, T2 arg2)
		{
			Event?.Invoke(arg1, arg2);
		}
	}

	internal class DelegateProxyOnClient<T1, T2, T3> : DelegateProxyOnClientBase
	{
		public DelegateProxyOnClient()
		{
		}

		public event Action<T1, T2, T3> Event;

		protected override bool IsEmpty => Event == null;

		public void FireEvent(T1 arg1, T2 arg2, T3 arg3)
		{
			Event?.Invoke(arg1, arg2, arg3);
		}
	}

	internal class DelegateProxyOnClient<T1, T2, T3, T4> : DelegateProxyOnClientBase
	{
		public DelegateProxyOnClient()
		{
		}

		public event Action<T1, T2, T3, T4> Event;

		protected override bool IsEmpty => Event == null;

		public void FireEvent(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
		{
			Event?.Invoke(arg1, arg2, arg3, arg4);
		}
	}
}
