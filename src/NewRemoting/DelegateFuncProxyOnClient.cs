using System;
using System.Collections.Generic;

namespace NewRemoting;

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<TOut>
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<TOut> Event;

	public TOut FireEvent()
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke() : default;
	}

	public bool IsEmpty()
	{
		return Event == null;
	}
}

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<T1, TOut>
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<T1, TOut> Event;

	public TOut FireEvent(T1 arg)
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke(arg) : default;
	}

	public bool IsEmpty()
	{
		return Event == null;
	}
}

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<T1, T2, TOut>
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<T1, T2, TOut> Event;

	public TOut FireEvent(T1 arg1, T2 arg2)
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke(arg1, arg2) : default;
	}

	public bool IsEmpty()
	{
		return Event == null;
	}
}

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<T1, T2, T3, TOut>
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<T1, T2, T3, TOut> Event;

	public TOut FireEvent(T1 arg1, T2 arg2, T3 arg3)
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke(arg1, arg2, arg3) : default;
	}

	public bool IsEmpty()
	{
		return Event == null;
	}
}

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateFuncProxyOnClient<T1, T2, T3, T4, TOut>
{
	public DelegateFuncProxyOnClient()
	{
	}

	public event Func<T1, T2, T3, T4, TOut> Event;

	public TOut FireEvent(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
	{
		var threadSafeEvent = Event;
		return threadSafeEvent != null ? threadSafeEvent.Invoke(arg1, arg2, arg3, arg4) : default;
	}

	public bool IsEmpty()
	{
		return Event == null;
	}
}
