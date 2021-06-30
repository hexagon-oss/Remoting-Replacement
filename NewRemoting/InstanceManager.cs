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
using System.Threading.Tasks;
using Castle.DynamicProxy;

namespace NewRemoting
{
	public class InstanceManager
	{
		private ConcurrentDictionary<string, InstanceInfo> _objects;

		static InstanceManager()
		{
			InstanceIdentifier = Environment.MachineName + "/" + Environment.ProcessId.ToString(CultureInfo.CurrentCulture);
		}

		public InstanceManager(ProxyGenerator proxyGenerator)
		{
			ProxyGenerator = proxyGenerator;
			_objects = new();
		}

		public ProxyGenerator ProxyGenerator
		{
			get;
		}

		/// <summary>
		/// This has a setter, because of the initialization sequence
		/// </summary>
		public IInterceptor Interceptor
		{
			get;
			set;
		}

		public static string InstanceIdentifier
		{
			get;
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

		public static bool IsLocalInstanceId(string objectId)
		{
			return objectId.StartsWith(InstanceIdentifier);
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
			Debug.WriteLine($"Created object reference with id {objectReference}");
			return objectReference;
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
		}

		public object CreateOrGetReferenceInstance(IInvocation invocation, bool canAttemptToInstantiate, Type typeOfArgument, string typeName, string objectId)
		{
			if (Interceptor == null)
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
				throw new InvalidOperationException("Got an instance that should be local but it isn't");
			}

			// Create a class proxy with all interfaces proxied as well.
			var interfaces = type.GetInterfaces();
			if (typeOfArgument != null && typeOfArgument.IsInterface)
			{
				Debug.WriteLine($"Create interface proxy for main type {typeOfArgument}");
				// If the call returns an interface, only create an interface proxy, because we might not be able to instantiate the actual class (because it's not public, it's sealed, has no public ctors, etc)
				instance = ProxyGenerator.CreateInterfaceProxyWithoutTarget(typeOfArgument, interfaces, Interceptor);
			}
			else if (canAttemptToInstantiate && (!type.IsSealed) && (MessageHandler.HasDefaultCtor(type) || (invocation != null && invocation.Arguments.Length > 0)))
			{
				Debug.WriteLine($"Create class proxy for main type {type}");
				// We can attempt to create a class proxy if we have ctor arguments and the type is not sealed
				instance = ProxyGenerator.CreateClassProxy(type, interfaces, ProxyGenerationOptions.Default, invocation.Arguments, Interceptor);
			}
			else if ((type.IsSealed || !MessageHandler.HasDefaultCtor(type)) && interfaces.Length > 0)
			{
				Debug.WriteLine($"Create interface proxy as backup for main type {type} with {interfaces[0]}");
				if (type.IsAssignableTo(typeof(Stream)))
				{
					// This is a bit of a special case, not sure yet for what other classes we should use this (otherwise, this gets an interface proxy for IDisposable, which is
					// not castable to Stream, which is most likely required)
					instance = ProxyGenerator.CreateClassProxy(typeof(Stream), interfaces, ProxyGenerationOptions.Default, Interceptor);
				}
				else
				{
					// Best would be to create a class proxy but we can't. So try an interface proxy with one of the interfaces instead
					instance = ProxyGenerator.CreateInterfaceProxyWithoutTarget(interfaces[0], interfaces, Interceptor);
				}
			}
			else
			{
				Debug.WriteLine($"Create class proxy as fallback for main type {type}");
				instance = ProxyGenerator.CreateClassProxy(type, interfaces, Interceptor);
			}

			AddInstance(instance, objectId);
			//if (!TryGetObjectFromId(objectId, out var inst2) || !ReferenceEquals(inst2, instance))
			//{
			//	throw new RemotingException("Couldn't add instance. This is an internal error.", RemotingExceptionKind.ProxyManagementError);
			//}

			Debug.WriteLine($"Created proxy instance for {instance.GetType()} with object id {objectId}");
			return instance;
		}
	}
}
