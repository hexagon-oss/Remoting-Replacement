using System;
using System.Collections.Generic;

namespace NewRemoting;

internal class DelegateProxyOnClient<T>
{
	public List<Delegate> Delegates { get; }

	public DelegateProxyOnClient()
	{
		Delegates = new List<Delegate>();
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

	public void RegisterDelegate(Delegate del)
	{
		Delegates.Add(del);
	}

	public bool UnregisterDelegate(Delegate del)
	{
		if (Delegates.Contains(del))
		{
			Delegates.Remove(del);
		}

		return Delegates.Count == 0;
	}
}
