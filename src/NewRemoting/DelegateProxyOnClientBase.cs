using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	internal abstract class DelegateProxyOnClientBase
	{
		protected DelegateProxyOnClientBase()
		{
			IsRegistered = false;
		}

		protected abstract bool IsEmpty
		{
			get;
		}

		/// <summary>
		/// True if this instance is registered on the server side
		/// </summary>
		protected bool IsRegistered
		{
			get;
			set;
		}

		/// <summary>
		/// Adds a delegate to this proxy.
		/// Can also add the delegate to another proxy, in case of a race condition
		/// </summary>
		/// <param name="instanceManager">The instance manager</param>
		/// <param name="delegateToRegister">The new delegate (callback method) to register in our instance</param>
		/// <param name="otherSideProcessId">The remote process</param>
		/// <param name="instanceId">The instance Id of the delegate</param>
		/// <returns>True when this instance is new or not registered on the server side, false otherwise</returns>
		public bool RegisterDelegate(InstanceManager instanceManager, Delegate delegateToRegister, string otherSideProcessId, string instanceId)
		{
			var addEventMethod = GetType().GetMethod("add_Event");
			// Try to add proxy to instance manager - this may return another, existing instance due to a race condition
			// In this case, we only need to register, like in the case where we already found the object in the list.
			var usedInstance = instanceManager.AddInstance(this, instanceId, otherSideProcessId, GetType(), false);
			DelegateProxyOnClientBase usedProxy = (DelegateProxyOnClientBase)usedInstance.QueryInstance();
			lock (usedProxy)
			{
				// Make sure to do this after the AddInstance above, to be sure to register on the correct proxy
				addEventMethod.Invoke(usedProxy, new[] { delegateToRegister });

				// if they're not equal, AddInstance returns something old instead of ourselves. In this case,
				// the operation is aborted here.
				if (!ReferenceEquals(usedProxy, this) && IsRegistered)
				{
					return false;
				}

				IsRegistered = true;
				return true;
			}
		}

		/// <summary>
		/// Removes the given delegate from this proxy.
		/// </summary>
		/// <param name="instanceManager">The instance manager</param>
		/// <param name="delegateToUnregister">The delegate (callback method) to unregister in our instance</param>
		/// <param name="otherSideProcessId">The remote process</param>
		/// <param name="instanceId">The instance Id of the delegate</param>
		/// <returns>True when the last delegate was deregistered and the server side callback can be disabled</returns>
		public bool RemoveDelegate(InstanceManager instanceManager, Delegate delegateToUnregister, string otherSideProcessId, string instanceId)
		{
			// Avoids a few bytes, and should be fine here, since class is internal.
			lock (this)
			{
				// Remove proxy class if this was the last client
				var proxyType = GetType(); // Important: This gets actual type, which is always a derived type.
				var removeEventMethod = proxyType.GetMethod("remove_Event");
				removeEventMethod.Invoke(this, new[] { delegateToUnregister });
				bool isEmpty = IsEmpty;
				if (isEmpty)
				{
					// Remove instance, but in a thread safe way!
					instanceManager.Remove(instanceId, otherSideProcessId, true);
				}
				else
				{
					// proxy is still needed, whole message can be aborted here
					return false;
				}

				IsRegistered = false;
				return true;
			}
		}
	}
}
