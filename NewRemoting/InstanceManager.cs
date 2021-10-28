using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;

namespace NewRemoting
{
	internal class InstanceManager
	{
		private readonly ILogger _logger;
		private readonly Dictionary<string, ClientSideInterceptor> _interceptors;
		private ConcurrentDictionary<string, InstanceInfo> _objects;

		static InstanceManager()
		{
			InstanceIdentifier = Environment.MachineName + ":" + Environment.ProcessId.ToString(CultureInfo.CurrentCulture);
		}

		public InstanceManager(ProxyGenerator proxyGenerator, ILogger logger)
		{
			_logger = logger;
			ProxyGenerator = proxyGenerator;
			_objects = new();
			_interceptors = new();
		}

		public static string InstanceIdentifier
		{
			get;
		}

		public ProxyGenerator ProxyGenerator
		{
			get;
		}

		/// <summary>
		/// This has a setter, because of the initialization sequence
		/// </summary>
		public static bool IsLocalInstanceId(string objectId)
		{
			return objectId.StartsWith(InstanceIdentifier);
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
			return interceptors[interceptorName];
		}

		public string GetIdForObject(object instance)
		{
			string id = CreateObjectInstanceId(instance);
			AddInstance(instance, id);
			return id;
		}

		public bool TryGetObjectFromId(string id, [NotNullWhen(true)]out object instance)
		{
			if (_objects.TryGetValue(id, out InstanceInfo value))
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

			_objects.AddOrUpdate(objectId, s => new InstanceInfo(instance, objectId), (s, info) => new InstanceInfo(instance, objectId));
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

			var values = _objects.Values.ToList();
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
				throw new InvalidOperationException($"Could not locate instance with ID {id} or it is not local");
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
			_objects.Clear();
		}

		public void PerformGc(BinaryWriter w)
		{
			// Would be good if we could synchronize our updates with the GC, but that appears to be a bit fuzzy and fails if the
			// GC is in concurrent mode.
			List<InstanceInfo> instancesToClear = new();
			foreach (var e in _objects)
			{
				// Iterating over a ConcurrentDictionary should be thread safe
				if (e.Value.IsReleased)
				{
					instancesToClear.Add(e.Value);
					_objects.TryRemove(e);
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

			public InstanceInfo(object obj, string identifier)
			{
				// If the actual instance lives in our process, we need to keep the hard reference, because
				// there are clients that may keep a reference to this object.
				// If it is a remote reference, we can use a weak reference. It will be gone, once there are no
				// other references to it within our process - meaning no one has a reference to the proxy any more.
				if (IsLocalInstanceId(identifier))
				{
					_instanceHardReference = obj;
				}
				else
				{
					_instanceWeakReference = new WeakReference(obj, false);
				}

				Identifier = identifier;
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
					//// Enable when TryGetObjectId is no more relying on this.
					////if (ret == null)
					////{
					////	throw new RemotingException($"Unable to recover instance of object id {Identifier}", RemotingExceptionKind.ProxyManagementError);
					////}

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

					throw new RemotingException("Unknown type found in argument stream", RemotingExceptionKind.ProxyManagementError);
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
			if (typeOfArgument != null && typeOfArgument.IsInterface)
			{
				_logger.Log(LogLevel.Debug, $"Create interface proxy for main type {typeOfArgument}");
				// If the call returns an interface, only create an interface proxy, because we might not be able to instantiate the actual class (because it's not public, it's sealed, has no public ctors, etc)
				instance = ProxyGenerator.CreateInterfaceProxyWithoutTarget(typeOfArgument, interfaces, interceptor);
			}
			else if (canAttemptToInstantiate && (!type.IsSealed) && (MessageHandler.HasDefaultCtor(type) || (invocation != null && invocation.Arguments.Length > 0)))
			{
				_logger.Log(LogLevel.Debug, $"Create class proxy for main type {type}");
				// We can attempt to create a class proxy if we have ctor arguments and the type is not sealed
				instance = ProxyGenerator.CreateClassProxy(type, interfaces, ProxyGenerationOptions.Default, invocation.Arguments, interceptor);
			}
			else if ((type.IsSealed || !MessageHandler.HasDefaultCtor(type)) && interfaces.Length > 0)
			{
				_logger.Log(LogLevel.Debug, $"Create interface proxy as backup for main type {type} with {interfaces[0]}");
				if (type.IsAssignableTo(typeof(Stream)))
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
			_objects.TryRemove(objectId, out _);
		}

		public void AddInterceptor(ClientSideInterceptor interceptor)
		{
			_interceptors.Add(interceptor.OtherSideInstanceId, interceptor);
		}
	}
}
