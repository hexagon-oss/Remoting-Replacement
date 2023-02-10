using System;
using System.Collections.Generic;

namespace NewRemoting;

/// <summary>
/// Delegate proxy on client to handle remote events - used only per reflection, therefore no usages visible
/// </summary>
internal class DelegateProxyOnClient<T>
{
	public DelegateProxyOnClient()
	{
	}

	public event Action<T> Event;

	public void FireEvent(T arg)
	{
		Event?.Invoke(arg);
	}

	public bool IsEmpty()
	{
		return Event == null;
	}
}

internal class DelegateProxyOnClient<T1, T2>
{
	public DelegateProxyOnClient()
	{
	}

	public event Action<T1, T2> Event;

	public void FireEvent(T1 arg1, T2 arg2)
	{
		Event?.Invoke(arg1, arg2);
	}

	public bool IsEmpty()
	{
		return Event == null;
	}
}

internal class DelegateProxyOnClient<T1, T2, T3>
{
	public DelegateProxyOnClient()
	{
	}

	public event Action<T1, T2, T3> Event;

	public void FireEvent(T1 arg1, T2 arg2, T3 arg3)
	{
		Event?.Invoke(arg1, arg2, arg3);
	}

	public bool IsEmpty()
	{
		return Event == null;
	}
}

internal class DelegateProxyOnClient<T1, T2, T3, T4>
{
	public DelegateProxyOnClient()
	{
	}

	public event Action<T1, T2, T3, T4> Event;

	public void FireEvent(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
	{
		Event?.Invoke(arg1, arg2, arg3, arg4);
	}

	public bool IsEmpty()
	{
		return Event == null;
	}
}
