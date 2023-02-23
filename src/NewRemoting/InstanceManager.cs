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
		private static readonly ConcurrentDictionary<string, InstanceInfo> s_objects;

		/// <summary>
		/// The list of known remote identifiers we have given references to.
		/// Key: Identifier, Value: Index
		/// </summary>
		private static readonly ConcurrentDictionary<string, int> s_knownRemoteInstances;

		private static int s_nextIndex;
		private static int s_numberOfInstancesUsed = 1;

		private readonly ILogger _logger;
		private readonly Dictionary<string, ClientSideInterceptor> _interceptors;

		static InstanceManager()
		{
			s_objects = new ConcurrentDictionary<string, InstanceInfo>();
			s_knownRemoteInstances = new ConcurrentDictionary<string, int>();
			s_nextIndex = -1;
		}

		public InstanceManager(ProxyGenerator proxyGenerator, ILogger logger)
		{
			_logger = logger;
			ProxyGenerator = proxyGenerator;
			_interceptors = new();
			InstanceIdentifier = Environment.MachineName + ":" + Environment.ProcessId.ToString(CultureInfo.CurrentCulture) + "." + s_numberOfInstancesUsed++;
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
		/// Get a method identifier (basically the unique name of a remote method)
		/// </summary>
		/// <param name="me">The method to encode</param>
		/// <returns></returns>
		public static string GetMethodIdentifier(MethodInfo me)
		{
			StringBuilder id = new StringBuilder($"{me.GetType().FullName}/.M/{me.Name}");
			var gen = me.GetGenericArguments();
			if (gen.Length > 0)
			{
				id.Append('<');
				id.AppendJoin(',', gen.Select(x => x.FullName));
				id.Append('>');
			}

			var parameters = me.GetParameters();
			id.Append('(');
			id.AppendJoin(',', parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}"));
			foreach (var p in parameters)
			{
				id.Append($"/{p.ParameterType.FullName}|{p.Name}");
			}

			id.Append(')');

			return id.ToString();
		}

		/// <summary>
		/// This has a setter, because of the initialization sequence
		/// </summary>
		public bool IsLocalInstanceId(string objectId)
		{
			return objectId.StartsWith(InstanceIdentifier);
		}

		public string RegisterRealObjectAndGetId(object instance, string willBeSentTo)
		{
			string id = CreateObjectInstanceId(instance);
			AddInstance(instance, id, willBeSentTo, instance.GetType(), true);
			return id;
		}

		/// <summary>
		/// Create an identifier for a delegate target (method + instance)
		/// </summary>
		/// <param name="del">The delegate to identify</param>
		/// <param name="remoteInstanceId">The instance of the target class to call</param>
		/// <returns></returns>
		public string GetDelegateTargetIdentifier(Delegate del, string remoteInstanceId)
		{
			StringBuilder id = new StringBuilder(FormattableString.Invariant($"{InstanceIdentifier}/{del.Method.GetType().FullName}/.Method/{del.Method.Name}/I{remoteInstanceId}"));
			foreach (var g in del.Method.GetGenericArguments())
			{
				id.Append($"/{g.FullName}");
			}

			var parameters = del.Method.GetParameters();
			id.Append($"?{parameters.Length}");
			foreach (var p in parameters)
			{
				id.Append($"/{p.ParameterType.FullName}|{p.Name}");
			}

			if (del.Target != null)
			{
				id.Append($"/{RuntimeHelpers.GetHashCode(del.Target)}");
			}

			return id.ToString();
		}

		/// <summary>
		/// Get an actual instance from an object Id
		/// </summary>
		/// <param name="id">The object id</param>
		/// <param name="instance">Returns the object instance (this is normally a real instance and not a proxy, but this is not always true
		/// when transient servers exist)</param>
		/// <returns>True when an object with the given id was found, false otherwise</returns>
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

		public InstanceInfo AddInstance(object instance, string objectId, string willBeSentTo, Type originalType, bool doThrowOnDuplicate)
		{
			if (instance == null)
			{
				throw new ArgumentNullException(nameof(instance));
			}

			if (Client.IsProxyType(originalType))
			{
				throw new ArgumentException("The original type cannot be a proxy", nameof(originalType));
			}

			return s_objects.AddOrUpdate(objectId, s =>
			{
				// Not found in list - insert new info object
				var ii = new InstanceInfo(instance, objectId, IsLocalInstanceId(objectId), originalType, this);
				MarkInstanceAsInUseBy(willBeSentTo, ii);
				_logger.LogDebug($"Added new instance {ii.Identifier} to instance manager");
				return ii;
			}, (id, existingInfo) =>
			{
				// Update existing info object with new client information
				lock (existingInfo)
				{
					if (existingInfo.IsReleased)
					{
						// if marked as no longer needed, revive by setting the instance
						existingInfo.Instance = instance;
					}
					else
					{
						if (doThrowOnDuplicate && !ReferenceEquals(existingInfo.Instance, instance))
						{
							var msg = FormattableString.Invariant(
								$"Added new instance of {instance.GetType()} is not equals to {existingInfo.Identifier} to instance manager, but no duplicate was expected");
							_logger.LogError(msg);
							throw new InvalidOperationException(msg);
						}

						// We have created the new instance twice due to a race condition
						// drop it again and use the old one instead
						_logger.LogInformation($"Race condition detected: Duplicate instance for object id {objectId} will be discarded.");
					}

					// Update existing living info object with new client information
					MarkInstanceAsInUseBy(willBeSentTo, existingInfo);
					return existingInfo;
				}
			});
		}

		/// <summary>
		/// Gets the instance id for a given object.
		/// This method is slow - should be improved by a reverse dictionary or similar (maybe use <see cref="ConditionalWeakTable{TKey,TValue}"/>)
		/// </summary>
		public bool TryGetObjectId(object instance, out string instanceId, out Type originalType)
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
					originalType = v.OriginalType;
					return true;
				}
			}

			instanceId = null;
			originalType = null;
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

		[Obsolete("Unittest only")]
		internal InstanceInfo QueryInstanceInfo(string id)
		{
			return s_objects[id];
		}

		/// <summary>
		/// Completely clears this instance. Only to be used for testing purposes
		/// </summary>
		/// <param name="fullyClear">Pass in true</param>
		internal void Clear(bool fullyClear)
		{
			Clear();
			if (fullyClear)
			{
				s_objects.Clear();
				s_knownRemoteInstances.Clear();
				s_numberOfInstancesUsed = 1;
				s_nextIndex = -1;
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
				lock (e.Value)
				{
					// Iterating over a ConcurrentDictionary should be thread safe
					if (e.Value.IsReleased || (dropAll && e.Value.Owner == this))
					{
						if (e.Value.IsLocal == false)
						{
							instancesToClear.Add(e.Value);
						}

						_logger.LogDebug($"Instance {e.Value.Identifier} is released locally");
						MarkInstanceAsUnusedLocally(e.Value.Identifier);
					}
				}
			}

			if (instancesToClear.Count == 0)
			{
				return;
			}

			_logger.Log(LogLevel.Debug, $"Cleaning up references to {instancesToClear.Count} objects");
			RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.GcCleanup, 0);
			using (var lck = hd.WriteHeader(w))
			{
				w.Write(instancesToClear.Count);
				foreach (var x in instancesToClear)
				{
					w.Write(x.Identifier);
				}
			}
		}

		/// <summary>
		/// Mark instance as unused and remove it if possible - needs to be called with lock on ii
		/// </summary>
		private void MarkInstanceAsUnusedLocally(string id)
		{
			if (s_objects.TryGetValue(id, out var ii))
			{
				ii.MarkAsUnusedViaRemoting();
				// Instances that are managed on the server side shall not be removed,
				// because others might just ask for them.
				if (!ii.IsReleased)
				{
					return;
				}

				if (ii.ReferenceBitVector != 0)
				{
					_logger.LogError($"Instance {ii.Identifier} has inconsistent state preventing removal from the instance manager");
					return;
				}
			}

			if (s_objects.TryRemove(id, out ii))
			{
				if (ii.ReferenceBitVector != 0 || ii.IsReleased == false)
				{
					throw new InvalidOperationException(FormattableString.Invariant($"Attempting to free a reference ({ii.Identifier}) that is still in use"));
				}

				_logger.LogDebug($"Instance {ii.Identifier} is removed from the instance manager");
			}
		}

		public object CreateOrGetProxyForObjectId(IInvocation invocation, bool canAttemptToInstantiate, Type typeOfArgument, string typeName, string objectId)
		{
			if (!_interceptors.Any())
			{
				throw new InvalidOperationException("Interceptor not set. Invalid initialization sequence");
			}

			object instance;
			Type type = string.IsNullOrEmpty(typeName) ? null : Server.GetTypeFromAnyAssembly(typeName);
			if (type == null)
			{
				// The type name may be omitted if the client knows that this instance must exist
				// (i.e. because it is sending a reference to a proxy back)
				if (TryGetObjectFromId(objectId, out instance))
				{
					_logger.LogDebug($"Found an instance for object id {objectId}");
					return instance;
				}

				throw new RemotingException("Unknown type found in argument stream");
			}
			else
			{
				if (TryGetObjectFromId(objectId, out instance))
				{
					_logger.LogDebug($"Found an instance for object id {objectId}");
					return instance;
				}
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
				if (MessageHandler.HasDefaultCtor(type))
				{
					instance = ProxyGenerator.CreateClassProxy(type, interfaces, ProxyGenerationOptions.Default, Array.Empty<object>(), interceptor);
				}
				else
				{
					// We can attempt to create a class proxy if we have ctor arguments and the type is not sealed. But only if we are really calling into a ctor, otherwise the invocation
					// arguments are the method arguments that created this instance as return value and then obviously the arguments are different.
					instance = ProxyGenerator.CreateClassProxy(type, interfaces, ProxyGenerationOptions.Default, invocation.Arguments, interceptor);
				}
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
					// As of .NET6.0, we need to create a real local instance, since creating a fake handle no longer works.
					string mySelf = Assembly.GetExecutingAssembly().Location;
					instance = ProxyGenerator.CreateClassProxy(typeof(FileStream), interfaces, ProxyGenerationOptions.Default,
						new object[] { mySelf, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 }, interceptor);
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

			InstanceInfo inst = AddInstance(instance, objectId, null, type, false);

			return inst.Instance;
		}

		public void Remove(string objectId, string remoteInstanceIdentifier)
		{
			if (!s_knownRemoteInstances.TryGetValue(remoteInstanceIdentifier, out int id))
			{
				// Not a known remote instance, cannot have any objects
				return;
			}

			ulong bit = GetBitFromIndex(id);

			if (s_objects.TryGetValue(objectId, out InstanceInfo ii))
			{
				lock (ii)
				{
					ii.ReferenceBitVector &= ~bit;
					if (ii.ReferenceBitVector == 0)
					{
						// If not more clients, forget about this object - the server GC will care for the rest.
						MarkInstanceAsUnusedLocally(ii.Identifier);
					}
				}
			}
		}

		public void AddInterceptor(ClientSideInterceptor interceptor)
		{
			_interceptors.Add(interceptor.OtherSideInstanceId, interceptor);
		}

		private void MarkInstanceAsInUseBy(string willBeSentTo, InstanceInfo instanceInfo)
		{
			if (!instanceInfo.IsLocal)
			{
				// Only local instances need reference "counting"
				return;
			}

			if (willBeSentTo == null)
			{
				throw new ArgumentNullException(nameof(willBeSentTo));
			}

			int indexOfClient = s_knownRemoteInstances.AddOrUpdate(willBeSentTo, (s) =>
			{
				return Interlocked.Increment(ref s_nextIndex);
			}, (s, i) => i);

			// To save memory and processing time, we use a bitvector to keep track of which client has a reference to
			// a specific instance
			if (s_nextIndex > 8 * sizeof(UInt64))
			{
				_logger.LogWarning($"Too many instances registered {0}", s_nextIndex);
				foreach (var i in s_knownRemoteInstances)
				{
					_logger.LogWarning(i.Key);
				}

				throw new InvalidOperationException("To many different instance identifiers seen - only up to 64 allowed right now");
			}

			ulong bit = GetBitFromIndex(indexOfClient);
			instanceInfo.ReferenceBitVector |= bit;
		}

		private ulong GetBitFromIndex(int index)
		{
			return 1ul << index;
		}

		internal class InstanceInfo
		{
			private readonly InstanceManager _owningInstanceManager;
			private object _instanceHardReference;
			private WeakReference _instanceWeakReference;

			public InstanceInfo(object obj, string identifier, bool isLocal, Type originalType, InstanceManager owner)
			{
				IsLocal = isLocal;
				Instance = obj;

				Identifier = identifier;
				OriginalType = originalType ?? throw new ArgumentNullException(nameof(originalType));
				_owningInstanceManager = owner;
				ReferenceBitVector = 0;
			}

			/// <summary>
			/// If the actual instance lives in our process, we need to keep the hard reference, because
			/// there are clients that may keep a reference to this object.
			/// If it is a remote reference, we can use a weak reference. It will be gone, once there are no
			/// other references to it within our process - meaning no one has a reference to the proxy any more.
			/// </summary>
			public object Instance
			{
				get
				{
					if (_instanceHardReference != null)
					{
						return _instanceHardReference;
					}

					var ret = _instanceWeakReference?.Target;

					if (IsLocal)
					{
						// If this should be a hard reference, resurrect it
						Resurrect();
					}

					return ret;
				}
				set
				{
					if (IsLocal)
					{
						_instanceHardReference = value;
						_instanceWeakReference = null;
					}
					else
					{
						_instanceWeakReference = new WeakReference(value, false);
					}
				}
			}

			public string Identifier
			{
				get;
			}

			public bool IsLocal { get; }

			public Type OriginalType
			{
				get;
			}

			public bool IsReleased
			{
				get
				{
					return _instanceHardReference == null && (_instanceWeakReference == null || !_instanceWeakReference.IsAlive);
				}
			}

			public InstanceManager Owner => _owningInstanceManager;

			/// <summary>
			/// Contains a binary 1 for each remote instance from the <see cref="InstanceManager.s_knownRemoteInstances"/> that has
			/// references to this instance. If this is 0, the object is eligible for garbage collection from the view of the
			/// remoting infrastructure.
			/// </summary>
			public UInt64 ReferenceBitVector
			{
				get;
				set;
			}

			public void MarkAsUnusedViaRemoting()
			{
				if (_instanceHardReference != null)
				{
					_instanceWeakReference = new WeakReference(_instanceHardReference, false);
					_instanceHardReference = null;
				}
			}

			public bool Resurrect()
			{
				object instance = _instanceWeakReference?.Target;
				_instanceHardReference = instance;
				_instanceWeakReference = null;
				return instance != null;
			}
		}
	}
}
