using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace NewRemoting
{
	internal class InstanceManager
	{
		/// <summary>
		/// The global(!) object registry. Contains references to all objects involved in remoting.
		/// The instances can be local, in which case we use it to look up their ids, or remote, in
		/// which case we use it to look up the correct proxy.
		/// </summary>
		private static ConcurrentDictionary<string, InstanceInfo> s_objects;

		private static int _numberOfInstancesUsed = 1;
		private readonly ILogger _logger;
		private readonly Dictionary<string, ClientSideInterceptor> _interceptors;

		static InstanceManager()
		{
			s_objects = new ConcurrentDictionary<string, InstanceInfo>();
		}

		public InstanceManager(ProxyGenerator proxyGenerator, ILogger logger)
		{
			_logger = logger;
			ProxyGenerator = proxyGenerator;
			_interceptors = new();
			InstanceIdentifier = Environment.MachineName + ":" + Environment.ProcessId.ToString(CultureInfo.CurrentCulture) + "." + _numberOfInstancesUsed++;
		}

		/// <summary>
		/// A Destructor, to make sure the static list is properly cleaned up
		/// </summary>
		~InstanceManager()
		{
			Dispose(false);
		}

		public string InstanceIdentifier
		{
			get;
		}

		public ProxyGenerator ProxyGenerator
		{
			get;
		}

		public static ClientSideInterceptor GetInterceptor(Dictionary<string, ClientSideInterceptor> interceptors, string objectId)
		{
			// When first starting the client, we don't know the other side's instance id, so we use
			// an empty string for it. The client should only ever have this one entry in the list, anyway
			if (interceptors.TryGetValue(string.Empty, out var other))
			{
				return other;
			}

			// TODO: Since the list of interceptors is typically small, iterating may be faster
			string interceptorName = objectId.Substring(0, objectId.IndexOf("/", StringComparison.Ordinal));
			if (interceptors.TryGetValue(interceptorName, out var ic))
			{
				return ic;
			}
			else if (interceptors.Count >= 1)
			{
				// If the above fails, we assume the instance lives on a third system.
				// Here, we assume the first (or only) remote connection is the master one and the only one that can lead to further connections
				return interceptors.First().Value;
			}

			throw new InvalidOperationException("No interceptors available");
		}

		/// <summary>
		/// This has a setter, because of the initialization sequence
		/// </summary>
		public bool IsLocalInstanceId(string objectId)
		{
			return objectId.StartsWith(InstanceIdentifier);
		}

		public string GetIdForObject(object instance)
		{
			string id = CreateObjectInstanceId(instance);
			AddInstance(instance, id);
			return id;
		}

		public bool TryGetObjectFromId(string id, [NotNullWhen(true)]out object instance)
		{
			if (s_objects.TryGetValue(id, out InstanceInfo value))
			{
				if (value.Instance != null)
				{
					instance = value.Instance;
					return true;
				}
			}

			instance = null;
			return false;
		}

		public void AddInstance(object instance, string objectId)
		{
			if (instance == null)
			{
				throw new ArgumentNullException(nameof(instance));
			}

			s_objects.AddOrUpdate(objectId, s => new InstanceInfo(instance, objectId, IsLocalInstanceId(objectId), this), (s, info) => new InstanceInfo(instance, objectId, IsLocalInstanceId(objectId), this));
		}

		/// <summary>
		/// Gets the instance id for a given object.
		/// This method is slow - should be improved by a reverse dictionary or similar (maybe use <see cref="ConditionalWeakTable{TKey,TValue}"/>)
		/// </summary>
		public bool TryGetObjectId(object instance, out string instanceId)
		{
			if (ReferenceEquals(instance, null))
			{
				throw new ArgumentNullException(nameof(instance));
			}

			var values = s_objects.Values.ToList();
			foreach (var v in values)
			{
				if (ReferenceEquals(v.Instance, instance))
				{
					instanceId = v.Identifier;
					return true;
				}
			}

			instanceId = null;
			return false;
		}

		public object GetObjectFromId(string id)
		{
			if (!TryGetObjectFromId(id, out object instance))
			{
				throw new InvalidOperationException($"Could not locate instance with ID {id} or it is not local. Local identifier: {InstanceIdentifier}");
			}

			return instance;
		}

		private string CreateObjectInstanceId(object obj)
		{
			string objectReference = FormattableString.Invariant($"{InstanceIdentifier}/{obj.GetType().FullName}/{RuntimeHelpers.GetHashCode(obj)}");
			return objectReference;
		}

		public void Clear()
		{
			foreach (var o in s_objects)
			{
				if (o.Value.Owner == this)
				{
					s_objects.TryRemove(o);
				}
			}
		}

		protected virtual void Dispose(bool disposing)
		{
			Clear(); // Call whether disposing is true or not!
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public void PerformGc(BinaryWriter w)
		{
			PerformGc(w, false);
		}

		/// <summary>
		/// Checks for dead references in our instance cache and tells the server to clean them up.
		/// </summary>
		/// <param name="w">The link to the server</param>
		/// <param name="dropAll">True to drop all references, including active ones (used if about to disconnect)</param>
		public void PerformGc(BinaryWriter w, bool dropAll)
		{
			// Would be good if we could synchronize our updates with the GC, but that appears to be a bit fuzzy and fails if the
			// GC is in concurrent mode.
			List<InstanceInfo> instancesToClear = new();
			foreach (var e in s_objects)
			{
				// Iterating over a ConcurrentDictionary should be thread safe
				if (e.Value.IsReleased || dropAll)
				{
					instancesToClear.Add(e.Value);
					s_objects.TryRemove(e);
				}
			}

			if (instancesToClear.Count == 0)
			{
				return;
			}

			_logger.Log(LogLevel.Debug, $"Cleaning up references to {instancesToClear.Count} objects");
			RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.GcCleanup, 0);
			hd.WriteTo(w);
			w.Write(instancesToClear.Count);
			foreach (var x in instancesToClear)
			{
				w.Write(x.Identifier);
			}
		}

		private class InstanceInfo
		{
			private readonly object _instanceHardReference;
			private readonly WeakReference _instanceWeakReference;
			private readonly InstanceManager _owningInstanceManager;

			public InstanceInfo(object obj, string identifier, bool isLocal, InstanceManager owner)
			{
				// If the actual instance lives in our process, we need to keep the hard reference, because
				// there are clients that may keep a reference to this object.
				// If it is a remote reference, we can use a weak reference. It will be gone, once there are no
				// other references to it within our process - meaning no one has a reference to the proxy any more.
				if (isLocal)
				{
					_instanceHardReference = obj;
				}
				else
				{
					_instanceWeakReference = new WeakReference(obj, false);
				}

				Identifier = identifier;
				_owningInstanceManager = owner;
			}

			public object Instance
			{
				get
				{
					if (_instanceHardReference != null)
					{
						return _instanceHardReference;
					}

					var ret = _instanceWeakReference?.Target;

					return ret;
				}
			}

			public string Identifier
			{
				get;
			}

			public bool IsReleased
			{
				get
				{
					return _instanceHardReference == null && !_instanceWeakReference.IsAlive;
				}
			}

			public InstanceManager Owner => _owningInstanceManager;
		}

		public object CreateOrGetReferenceInstance(IInvocation invocation, bool canAttemptToInstantiate, Type typeOfArgument, string typeName, string objectId)
		{
			if (!_interceptors.Any())
			{
				throw new InvalidOperationException("Interceptor not set. Invalid initialization sequence");
			}

			object instance;
			Type type = string.IsNullOrEmpty(typeName) ? null : Server.GetTypeFromAnyAssembly(typeName);
			switch (type)
			{
				case null:
					// The type name may be omitted if the client knows that this instance must exist
					// (i.e. because it is sending a reference to a proxy back)
					if (TryGetObjectFromId(objectId, out instance))
					{
						return instance;
					}

					throw new RemotingException("Unknown type found in argument stream");
			}

			if (TryGetObjectFromId(objectId, out instance))
			{
				return instance;
			}

			if (IsLocalInstanceId(objectId))
			{
				throw new InvalidOperationException("Got an instance that should be proxied, but it is a local object");
			}

			var interceptor = GetInterceptor(_interceptors, objectId);
			// Create a class proxy with all interfaces proxied as well.
			var interfaces = type.GetInterfaces();
			ManualInvocation mi = invocation as ManualInvocation;
			if (typeOfArgument != null && typeOfArgument.IsInterface)
			{
				_logger.Log(LogLevel.Debug, $"Create interface proxy for main type {typeOfArgument}");
				// If the call returns an interface, only create an interface proxy, because we might not be able to instantiate the actual class (because it's not public, it's sealed, has no public ctors, etc)
				instance = ProxyGenerator.CreateInterfaceProxyWithoutTarget(typeOfArgument, interfaces, interceptor);
			}
			else if (canAttemptToInstantiate && (!type.IsSealed) && (MessageHandler.HasDefaultCtor(type) || (mi != null && invocation.Arguments.Length > 0 && mi.Constructor != null)))
			{
				_logger.Log(LogLevel.Debug, $"Create class proxy for main type {type}");
				// We can attempt to create a class proxy if we have ctor arguments and the type is not sealed. But only if we are really calling into a ctor, otherwise the invocation
				// arguments are the method arguments that created this instance as return value and then obviously the arguments are different.
				instance = ProxyGenerator.CreateClassProxy(type, interfaces, ProxyGenerationOptions.Default, invocation.Arguments, interceptor);
			}
			else if ((type.IsSealed || !MessageHandler.HasDefaultCtor(type) || typeOfArgument == typeof(object)) && interfaces.Length > 0)
			{
				// If the type is sealed or has no default ctor, we need to create an interface proxy, even if the target type is not an interface and may therefore not match.
				// If the target type is object, we also try an interface proxy instead, since everything should be convertible to object.
				_logger.Log(LogLevel.Debug, $"Create interface proxy as backup for main type {type} with {interfaces[0]}");
				if (type == typeof(FileStream))
				{
					// Special case of the Stream case below. This is not a general solution, but for this type, we can then create the correct type, so when
					// it is casted or marshalled again, it gets the correct proxy type.
					SafeFileHandle handle = new SafeFileHandle(new IntPtr(1), true);
					instance = ProxyGenerator.CreateClassProxy(typeof(FileStream), interfaces, ProxyGenerationOptions.Default,
						new object[] { handle, FileAccess.Read, 1024 }, interceptor);
					handle.SetHandleAsInvalid(); // Do not attempt to later free this handle
				}
				else if (type.IsAssignableTo(typeof(Stream)))
				{
					// This is a bit of a special case, not sure yet for what other classes we should use this (otherwise, this gets an interface proxy for IDisposable, which is
					// not castable to Stream, which is most likely required)
					instance = ProxyGenerator.CreateClassProxy(typeof(Stream), interfaces, ProxyGenerationOptions.Default, interceptor);
				}
				else if (type.IsAssignableTo(typeof(WaitHandle)))
				{
					instance = ProxyGenerator.CreateClassProxy(typeof(WaitHandle), interfaces, ProxyGenerationOptions.Default, interceptor);
				}
				else
				{
					// Best would be to create a class proxy but we can't. So try an interface proxy with one of the interfaces instead
					instance = ProxyGenerator.CreateInterfaceProxyWithoutTarget(interfaces[0], interfaces, interceptor);
				}
			}
			else
			{
				_logger.Log(LogLevel.Debug, $"Create class proxy as fallback for main type {type}");
				instance = ProxyGenerator.CreateClassProxy(type, interfaces, interceptor);
			}

			AddInstance(instance, objectId);

			return instance;
		}

		public void Remove(string objectId)
		{
			// Just forget about this object - the server GC will care for the rest.
			s_objects.TryRemove(objectId, out _);
		}

		public void AddInterceptor(ClientSideInterceptor interceptor)
		{
			_interceptors.Add(interceptor.OtherSideInstanceId, interceptor);
		}
	}
}
