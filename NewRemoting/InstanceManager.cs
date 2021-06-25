using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

		public InstanceManager(ProxyGenerator proxyGenerator)
		{
			InstanceIdentifier = Environment.MachineName + "/"+Environment.ProcessId.ToString(CultureInfo.CurrentCulture);
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

		public string InstanceIdentifier
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

		public bool IsLocalInstanceId(string objectId)
		{
			return objectId.StartsWith(InstanceIdentifier);
		}

		public void AddInstance(object instance, string objectId)
		{
			_objects.AddOrUpdate(objectId, s => new InstanceInfo(instance, objectId), (s, info) => new InstanceInfo(instance, objectId));
		}

		/// <summary>
		/// This method is slow and should only be used for debugging purposes (invariant validation)
		/// </summary>
		public bool TryGetObjectId(object instance, out string instanceId)
		{
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
			public InstanceInfo(object obj, string identifier)
			{
				Instance = obj;
				Identifier = identifier;
			}

			public object Instance
			{
				get;
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
				// If the call returns an interface, only create an interface proxy, because we might not be able to instantiate the actual class (because it's not public, it's sealed, has no public ctors, etc)
				instance = ProxyGenerator.CreateInterfaceProxyWithoutTarget(typeOfArgument, interfaces, Interceptor);
			}
			else if (canAttemptToInstantiate && (!type.IsSealed) && (MessageHandler.HasDefaultCtor(type) || (invocation != null && invocation.Arguments.Length > 0)))
			{
				// We can attempt to create a class proxy if we have ctor arguments and the type is not sealed
				instance = ProxyGenerator.CreateClassProxy(type, interfaces, ProxyGenerationOptions.Default, invocation.Arguments, Interceptor);
			}
			else if ((type.IsSealed || !MessageHandler.HasDefaultCtor(type)) && interfaces.Length > 0)
			{
				// Best would be to create a class proxy but we can't. So try an interface proxy with one of the interfaces instead
				instance = ProxyGenerator.CreateInterfaceProxyWithoutTarget(interfaces[0], interfaces, Interceptor);
			}
			else
			{
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
