using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NewRemoting
{
	/// <summary>
	/// This interface is mainly intended for unit testing serialization in client applications
	/// </summary>
	public interface IInstanceManager
	{
		/// <summary>
		/// Get an actual instance from an object Id
		/// </summary>
		/// <param name="id">The object id</param>
		/// <param name="instance">Returns the object instance (this is normally a real instance and not a proxy, but this is not always true
		/// when transient servers exist)</param>
		/// <returns>True when an object with the given id was found, false otherwise</returns>
		bool TryGetObjectFromId(string id, out object instance);

		object AddInstance(object instance, string objectId, string willBeSentTo, Type originalType, string originalTypeName, bool doThrowOnDuplicate);

		/// <summary>
		/// Gets the instance id for a given object.
		/// </summary>
		bool TryGetObjectId(object instance, out string instanceId, out string originalTypeName);

		/// <summary>
		/// Returns the object for a given id
		/// </summary>
		/// <param name="id">The object id</param>
		/// <param name="typeOfCallerName">The type of caller (only used for debugging purposes)</param>
		/// <param name="methodId">The method about to call (only used for debugging)</param>
		/// <param name="wasDelegateTarget">True if the <paramref name="id"/> references a delegate target, but is no longer present (rare
		/// race condition when a callback happens at the same time the event is disconnected)</param>
		/// <returns>The object from the global cache</returns>
		/// <exception cref="InvalidOperationException">The object didn't exist (unless it was a delegate target call)</exception>
		object GetObjectFromId(string id, string typeOfCallerName, string methodId, out bool wasDelegateTarget);

		/// <summary>
		/// Completely clears this instance. Only to be used for testing purposes
		/// </summary>
		/// <param name="fullyClear">Pass in true</param>
		void Clear(bool fullyClear);

		string RegisterRealObjectAndGetId(object instance, string willBeSentTo);

		object CreateOrGetProxyForObjectId(bool canAttemptToInstantiate,
			Type typeOfArgument, string typeName, string objectId, List<string> knownInterfaceNames);
	}
}
