using System;
using System.Collections.Generic;

namespace NewRemoting;

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<TOut> : DelegateProxyOnClientBase
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<TOut> Event;

	protected override bool IsEmpty => Event == null;

	public TOut FireEvent()
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke() : default;
	}
}

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<T1, TOut> : DelegateProxyOnClientBase
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<T1, TOut> Event;

	protected override bool IsEmpty => Event == null;

	public TOut FireEvent(T1 arg)
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke(arg) : default;
	}
}

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<T1, T2, TOut> : DelegateProxyOnClientBase
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<T1, T2, TOut> Event;

	protected override bool IsEmpty => Event == null;

	public TOut FireEvent(T1 arg1, T2 arg2)
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke(arg1, arg2) : default;
	}
}

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<T1, T2, T3, TOut> : DelegateProxyOnClientBase
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<T1, T2, T3, TOut> Event;

	protected override bool IsEmpty => Event == null;

	public TOut FireEvent(T1 arg1, T2 arg2, T3 arg3)
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke(arg1, arg2, arg3) : default;
	}
}

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<T1, T2, T3, T4, TOut> : DelegateProxyOnClientBase
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<T1, T2, T3, T4, TOut> Event;

	protected override bool IsEmpty => Event == null;

	public TOut FireEvent(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke(arg1, arg2, arg3, arg4) : default;
	}
}
