using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;
using NewRemoting.Toolkit;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace NewRemoting
{
	/// <summary>
	/// This class is responsible for encoding and decoding remote calls.
	/// It encodes the arguments when invoking a remote method and decodes them again on the server side.
	/// </summary>
	internal class MessageHandler
	{
		private static readonly object ConcurrentOperationsLock = new object();
		private readonly InstanceManager _instanceManager;
		private readonly FormatterFactory _formatterFactory;
		private readonly Dictionary<string, ClientSideInterceptor> _interceptors;
		private readonly Encoding _stringEncoding;
		private readonly ILogger _logger;
		private bool _initialized;
		private ConcurrentDictionary<RemotingReferenceType, uint> _stats;

		public MessageHandler(InstanceManager instanceManager, FormatterFactory formatterFactory, ILogger logger)
		{
			_stringEncoding = Encoding.Unicode;
			_instanceManager = instanceManager;
			_formatterFactory = formatterFactory;
			_initialized = false;
			_interceptors = new();
			_stats = new ConcurrentDictionary<RemotingReferenceType, uint>();
			foreach (RemotingReferenceType refType in Enum.GetValues(typeof(RemotingReferenceType)))
			{
				_stats[refType] = 0;
			}

			_logger = logger;
		}

		public void PrintStats()
		{
			_logger.LogInformation("Remoting Messagehandler usage stats:");
			foreach (var stat in _stats)
			{
				_logger.LogInformation(FormattableString.Invariant($"{stat.Key}: {stat.Value}"));
			}
		}

		public static Encoding DefaultStringEncoding => Encoding.UTF8;

		public InstanceManager InstanceManager => _instanceManager;

		public static bool HasDefaultCtor(Type t)
		{
			var ctor = t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, CallingConventions.Any, Type.EmptyTypes, null);
			return ctor != null;
		}

		private void LogMsg(RemotingReferenceType msgType)
		{
			_stats[msgType]++;
		}

		/// <summary>
		/// List of objects that should use marshalbyref semantics.
		/// The later requires serialization instead of by-reference semantics (note that in the future we might need a
		/// list of objects that _should be_ MarshalByRefObject but are technically not, since new BCL objects don't get that dependency)
		/// </summary>
		/// <param name="t">A type</param>
		/// <returns>True or false</returns>
		public static bool IsMarshalByRefType(Type t)
		{
			return t.IsAssignableTo(typeof(MarshalByRefObject)) && t != typeof(PooledMemoryStream);
		}

		/// <summary>
		/// Handles the given delegate-type argument by using a local proxy in between. Addition of delegate - it does not exist, the proxy
		/// is created and sent to the server, otherwise no action is needed aside from registering the delegate on the proxy.
		/// Similarly removal of a delegates is handled.
		/// </summary>
		/// <param name="w">The data sink</param>
		/// <param name="del">The object to write</param>
		/// <param name="calledMethod">The method that was called (typically add_(EventName) or remove_(EventName))</param>
		/// <param name="otherSideProcessId">Destination process identifier (used to keep track of references that are eventually encoded in the stream)</param>
		/// <param name="remoteInstanceId">The instance of the remote object the delegate operation was called on</param>
		/// <returns>True if the operation needs to be forwarded to the server (new delegate)</returns>
		public bool WriteDelegateArgumentToStream(BinaryWriter w, Delegate del, MethodInfo calledMethod,
			string otherSideProcessId, string remoteInstanceId)
		{
			if (calledMethod.IsStatic)
			{
				throw new InvalidRemotingOperationException("Can only register instance methods as delegate targets");
			}

			// The argument is a function pointer (typically the argument to a add_ or remove_ event)
			if (calledMethod.IsSpecialName && calledMethod.Name.StartsWith("add_", StringComparison.Ordinal))
			{
				if (del.Target != null)
				{
					string instanceId = _instanceManager.GetDelegateTargetIdentifier(del, remoteInstanceId);

					// Create a proxy class that provides a matching event for our delegate
					Type proxyType;
					var arguments = del.Method.GetParameters()
						.Select(x => x.ParameterType).ToList();
					bool hasReturnValue;
					if (del.Method.ReturnType != typeof(void))
					{
						hasReturnValue = true;
						arguments.Add((Type)del.Method.ReturnType);
						switch (arguments.Count)
						{
							case 1:
								proxyType = typeof(DelegateFuncProxyOnClient<>)
									.MakeGenericType(arguments.ToArray());
								break;
							case 2:
								proxyType =
									typeof(DelegateFuncProxyOnClient<,>).MakeGenericType(arguments.ToArray());
								break;
							case 3:
								proxyType =
									typeof(DelegateFuncProxyOnClient<,,>).MakeGenericType(arguments.ToArray());
								break;
							case 4:
								proxyType =
									typeof(DelegateFuncProxyOnClient<,,,>).MakeGenericType(arguments.ToArray());
								break;
							case 5:
								proxyType =
									typeof(DelegateFuncProxyOnClient<,,,,>).MakeGenericType(arguments.ToArray());
								break;
							default:
								throw new InvalidRemotingOperationException(
									$"Unsupported number of arguments for function ({arguments.Count}");
						}
					}
					else
					{
						hasReturnValue = false;
						switch (arguments.Count)
						{
							case 0:
								proxyType = typeof(DelegateProxyOnClient);
								break;
							case 1:
								proxyType = typeof(DelegateProxyOnClient<>).MakeGenericType(arguments.ToArray());
								break;
							case 2:
								proxyType = typeof(DelegateProxyOnClient<,>).MakeGenericType(arguments.ToArray());
								break;
							case 3:
								proxyType = typeof(DelegateProxyOnClient<,,>).MakeGenericType(arguments.ToArray());
								break;
							case 4:
								proxyType = typeof(DelegateProxyOnClient<,,,>).MakeGenericType(arguments.ToArray());
								break;
							default:
								throw new InvalidRemotingOperationException(
									$"Unsupported number of arguments for action ({arguments.Count}");
						}
					}

					var proxy = (DelegateProxyOnClientBase)Activator.CreateInstance(proxyType);
					if (!proxy.RegisterDelegate(_instanceManager, del, otherSideProcessId, instanceId))
					{
						return false;
					}

					w.Write((int)RemotingReferenceType.AddEvent);
					LogMsg(RemotingReferenceType.AddEvent);
					w.Write(instanceId);
					w.Write(hasReturnValue);
					// If the type of the delegate method is generic, we need to provide its type arguments
					w.Write(arguments.Count);
					foreach (var argType in arguments)
					{
						string arg = argType.AssemblyQualifiedName;
						if (arg == null)
						{
							throw new InvalidRemotingOperationException(
								"Unresolved generic type or some other undefined case");
						}

						w.Write(arg);
					}
				}
				else
				{
					throw new InvalidRemotingOperationException("The delegate target is a static method");
				}
			}
			else if (calledMethod.IsSpecialName && calledMethod.Name.StartsWith("remove_", StringComparison.Ordinal))
			{
				if (del.Target != null)
				{
					string instanceId = _instanceManager.GetDelegateTargetIdentifier(del, remoteInstanceId);

					if (_instanceManager.TryGetObjectFromId(instanceId, out var existingDelegateProxy))
					{
						DelegateProxyOnClientBase b = (DelegateProxyOnClientBase)existingDelegateProxy;
						if (!b.RemoveDelegate(_instanceManager, del, otherSideProcessId, instanceId))
						{
							return false;
						}
					}

					w.Write((int)RemotingReferenceType.RemoveEvent);
					LogMsg(RemotingReferenceType.RemoveEvent);
					w.Write(instanceId);
				}
				else
				{
					// The delegate target is a static method
					throw new InvalidRemotingOperationException("The delegate target is a static method");
				}
			}
			else
			{
				w.Write((int)RemotingReferenceType.MethodPointer);
				if (del.Target != null)
				{
					string instanceId = _instanceManager.GetDelegateTargetIdentifier(del, remoteInstanceId);
					_instanceManager.AddInstance(del, instanceId, otherSideProcessId, del.GetType(), del.GetType().AssemblyQualifiedName, true);
					w.Write(instanceId);
				}
				else
				{
					// The delegate target is a static method
					throw new InvalidRemotingOperationException("The delegate target is a static method");
				}

				w.Write(del.Method.DeclaringType.AssemblyQualifiedName);
				w.Write(del.Method.MetadataToken);
				var generics = del.Method.GetGenericArguments();
				// If the type of the delegate method is generic, we need to provide its type arguments
				w.Write(generics.Length);
				foreach (var genericType in generics)
				{
					string arg = genericType.AssemblyQualifiedName;
					if (arg == null)
					{
						throw new InvalidRemotingOperationException("Unresolved generic type or some other undefined case");
					}

					w.Write(arg);
				}
			}

			return true;
		}

		/// <summary>
		/// Write the given object to the target stream.
		/// When it is a type that shall be transferred by value, it is serialized, otherwise a reference is added to the stream.
		/// That reference is then converted to a proxy instance on the other side.
		/// </summary>
		/// <param name="w">The data sink</param>
		/// <param name="data">The object to write</param>
		/// <param name="referencesWillBeSentTo">Destination identifier (used to keep track of references that are eventually encoded in the stream)</param>
		/// <param name="interfaceOnlyClient">Client doesn't have full type info - either send it or send the interface list or...?</param>
		public void WriteArgumentToStream(BinaryWriter w, object data, string referencesWillBeSentTo, bool interfaceOnlyClient)
		{
			if (!_initialized)
			{
				throw new RemotingException("Instance is not initialized");
			}

			if (ReferenceEquals(data, null))
			{
				w.Write((int)RemotingReferenceType.NullPointer);
				return;
			}

			Type t = data.GetType();
			if (data is Type type)
			{
				// System.Type (and arrays of that, see below) need special handling, because it is not serializable nor Marshal-By-Ref, but still
				// has an exact match on the remote side.
				// TODO: For interfaceOnlyClient, this works on interfaces only
				w.Write((int)RemotingReferenceType.InstanceOfSystemType);
				w.Write(type.AssemblyQualifiedName);
				LogMsg(RemotingReferenceType.InstanceOfSystemType);
			}
			else if (data is IPAddress address)
			{
				// IPAddress is not serializable, even though it is actually trivially-serializable
				w.Write((int)RemotingReferenceType.IpAddress);
				string s = address.ToString();
				w.Write(s);
				LogMsg(RemotingReferenceType.IpAddress);
			}
			else if (data is Type[] typeArray)
			{
				// TODO: For interfaceOnlyClient, this works on interfaces only
				w.Write((int)RemotingReferenceType.ArrayOfSystemType);
				w.Write(typeArray.Length);
				LogMsg(RemotingReferenceType.ArrayOfSystemType);
				for (int i = 0; i < typeArray.Length; i++)
				{
					if (typeArray[i] == null)
					{
						w.Write(String.Empty);
					}
					else
					{
						w.Write(typeArray[i].AssemblyQualifiedName);
					}
				}
			}
			else if (TypeIsContainerWithReference(data, out Type contentType))
			{
				var list = data as IEnumerable;
				w.Write((int)RemotingReferenceType.ContainerType);
				LogMsg(RemotingReferenceType.ContainerType);
				w.Write(data.GetType().AssemblyQualifiedName);
				w.Write(contentType.AssemblyQualifiedName);
				foreach (object obj in list)
				{
					// Recursively write the arguments
					w.Write(true);
					WriteArgumentToStream(w, obj, referencesWillBeSentTo, interfaceOnlyClient);
				}

				w.Write(false); // Terminate the array
			}
			else if (data is Delegate)
			{
				throw new InvalidRemotingOperationException(
					"Can not register delegate targets - use WriteDelegateArgumentToStream");
			}
			else if (Client.IsRemoteProxy(data))
			{
				// Proxies are never serializable
				if (!_instanceManager.TryGetObjectId(data, out string objectId, out string originalTypeName))
				{
					throw new RemotingException("A proxy has no existing reference");
				}

				LogMsg(RemotingReferenceType.RemoteReference);
				w.Write((int)RemotingReferenceType.RemoteReference);
				w.Write(objectId);
				w.Write(originalTypeName);
				if (interfaceOnlyClient)
				{
					var interfaces = data.GetType().GetInterfaces().Where(x => x.IsPublic).ToList();
					w.Write(interfaces.Count);
					foreach (var ip in interfaces)
					{
						w.Write(ip.AssemblyQualifiedName ?? string.Empty);
					}
				}
				else
				{
					w.Write(0);
				}
			}
			else if (IsMarshalByRefType(t))
			{
				string objectId = _instanceManager.RegisterRealObjectAndGetId(data, referencesWillBeSentTo);
				w.Write((int)RemotingReferenceType.RemoteReference);
				LogMsg(RemotingReferenceType.RemoteReference);
				w.Write(objectId);

				// If this is not a proxy, this should always work correctly
				Type typeToSend = Client.GetUnproxiedType(data);
				var assemblyQualitfiedTypeName = typeToSend.AssemblyQualifiedName ?? String.Empty;
				w.Write(assemblyQualitfiedTypeName);
				if (interfaceOnlyClient)
				{
					var interfaces = typeToSend.GetInterfaces().Where(x => x.IsPublic).ToList();
					w.Write(interfaces.Count);
					foreach (var ip in interfaces)
					{
						w.Write(ip.AssemblyQualifiedName ?? string.Empty);
					}
				}
				else
				{
					w.Write(0);
				}
			}
			else
			{
				if (!TryUseFastSerialization(w, t, data))
				{
					UseSlowJsonSerialization(w, data, referencesWillBeSentTo);
				}
			}
		}

		private void UseSlowJsonSerialization(BinaryWriter w, object data, string referencesWillBeSentTo)
		{
			Type t = data.GetType();

			LogMsg(RemotingReferenceType.Auto);
			w.Write((Int32)RemotingReferenceType.SerializedItem);
			if (t.AssemblyQualifiedName == null)
			{
				throw new RemotingException($"Type {t} has no valid AssemblyQualifiedName. Dynamic types can't be serialized");
			}

			w.Write(t.AssemblyQualifiedName);

			// I currently see no other way than doing this copy, because we need to write the length first, so we can extract the json correctly later
			// We could look for the '\r' termination character in the json, but that requires peeking on the reader's end, which is also not
			// possible on a network stream.
			MemoryStream ms = new MemoryStream();
			BinaryWriter w2 = new BinaryWriter(ms);
			try
			{
				var options = _formatterFactory.CreateOrGetFormatter(referencesWillBeSentTo);
				JsonSerializer.Serialize(w2.BaseStream, data, options);
				w.Write((int)ms.Length);
				ms.Position = 0;
				ms.CopyTo(w.BaseStream, 64 * 1024);
				_formatterFactory.FinalizeSerialization(w, options);
			}
			catch (Exception ex) when (ex is NotSupportedException || ex is JsonException)
			{
				throw new SerializationException($"Automatic serialization of type {data.GetType()} failed.", ex);
			}

			ms.Dispose();
		}

		internal bool TryUseFastSerialization(BinaryWriter w, Type objectType, object data)
		{
			switch (data)
			{
				case Int32 i32:
				{
					w.Write((int)RemotingReferenceType.Int32);
					LogMsg(RemotingReferenceType.Int32);
					w.Write(i32);
					return true;
				}

				case UInt32 u32:
				{
					w.Write((int)RemotingReferenceType.Uint32);
					LogMsg(RemotingReferenceType.Uint32);
					w.Write(u32);
					return true;
				}

				case bool b:
				{
					if (b)
					{
						w.Write((int)RemotingReferenceType.BoolTrue);
						LogMsg(RemotingReferenceType.BoolTrue);
					}
					else
					{
						w.Write((int)RemotingReferenceType.BoolFalse);
						LogMsg(RemotingReferenceType.BoolFalse);
					}

					return true;
				}

				case Int16 s16:
				{
					w.Write((int)RemotingReferenceType.Int16);
					LogMsg(RemotingReferenceType.Int16);
					w.Write(s16);
					return true;
				}

				case UInt16 u16:
				{
					w.Write((int)RemotingReferenceType.Uint16);
					LogMsg(RemotingReferenceType.Uint16);
					w.Write(u16);
					return true;
				}

				case sbyte i8:
				{
					w.Write((int)RemotingReferenceType.Int8);
					LogMsg(RemotingReferenceType.Int8);
					w.Write(i8);
					return true;
				}

				case byte u8:
				{
					w.Write((int)RemotingReferenceType.Uint8);
					LogMsg(RemotingReferenceType.Uint8);
					w.Write(u8);
					return true;
				}

				case float f32:
				{
					w.Write((int)RemotingReferenceType.Float);
					LogMsg(RemotingReferenceType.Float);
					w.Write(f32);
					return true;
				}

				case Int64 i64:
				{
					w.Write((int)RemotingReferenceType.Int64);
					LogMsg(RemotingReferenceType.Int64);
					w.Write(i64);
					return true;
				}

				case UInt64 u64:
				{
					w.Write((int)RemotingReferenceType.Uint64);
					LogMsg(RemotingReferenceType.Uint64);
					w.Write(u64);
					return true;
				}

				case double f64:
				{
					w.Write((int)RemotingReferenceType.Double);
					LogMsg(RemotingReferenceType.Double);
					w.Write(f64);
					return true;
				}

				case Half f16:
				{
					w.Write((int)RemotingReferenceType.Half);
					LogMsg(RemotingReferenceType.Half);
					w.Write(f16);
					return true;
				}

				case string s:
				{
					w.Write((int)RemotingReferenceType.String);
					LogMsg(RemotingReferenceType.String);
					if (s.Length == 0)
					{
						// Don't write any data for the empty string, because attempting to read 0 bytes blocks.
						w.Write(0);
						return true;
					}

					var buffer = ArrayPool<byte>.Shared.Rent(_stringEncoding.GetMaxByteCount(s.Length));
					Span<byte> bufferSpan = buffer.AsSpan();
					int numBytesUsed = _stringEncoding.GetBytes(s.AsSpan(), bufferSpan);
					w.Write(numBytesUsed);
					w.Write(bufferSpan.Slice(0, numBytesUsed));
					ArrayPool<byte>.Shared.Return(buffer);
					return true;
				}

				// This code is suspected of causing "Fatal error. Internal CLR error. (0x80131506)"
				// No idea why this should be different to using standard serialization. Or simulation, by the way.
				case byte[] byteArray:
				{
					w.Write((int)RemotingReferenceType.ByteArray);
					LogMsg(RemotingReferenceType.ByteArray);
					if (byteArray.Length == 0)
					{
						// See above
						w.Write(0);
						return true;
					}

					w.Write(byteArray.Length);
					w.Write(byteArray, 0, byteArray.Length);
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// True when this type implements <see cref="IList{T}" /> with T being <see cref="MarshalByRefObject"/>.
		/// </summary>
		private bool TypeIsContainerWithReference(object data, out Type type)
		{
			if (data is IList enumerable)
			{
				var paramType = enumerable.GetType();
				var args = paramType.GenericTypeArguments;
				if (args.Length == 1)
				{
					type = args[0];
					if (type.IsSubclassOf(typeof(MarshalByRefObject)))
					{
						return true;
					}

					// Otherwise, we have to test the contents.
					return ContentIsMarshalByRef(enumerable);
				}
				else if (args.Length > 1)
				{
					// Not currently supported
					type = null;
					return false;
				}
				else if (paramType.IsArray && paramType.GetElementType().IsValueType &&
						 paramType.GetElementType().IsPrimitive)
				{
					type = null;
					return false;
				}
				else
				{
					// Data implements IEnumerable, but not IEnumerable{T}.
					type = typeof(object);
					return ContentIsMarshalByRef(enumerable);
				}
			}

			type = null;
			return false;
		}

		private bool ContentIsMarshalByRef(IList enumerable)
		{
			foreach (var e in enumerable)
			{
				if (e == null)
				{
					continue;
				}

				Type t = e.GetType();
				if (t.IsSubclassOf(typeof(MarshalByRefObject)) || ProxyUtil.IsProxyType(t))
				{
					return true;
				}
			}

			return false;
		}

		public void ProcessCallResponse(IInvocation invocation, BinaryReader reader, string otherSideProcessId, bool interfaceOnlyClient)
		{
			if (!_initialized)
			{
				throw new RemotingException("Instance is not initialized");
			}

			MethodBase methodBase;
			// This is true if this is a reply to a CreateInstance call (invocation.Method cannot be a ConstructorInfo instance)
			if (invocation is ManualInvocation mi && mi.Method == null && mi.Constructor != null)
			{
				methodBase = mi.Constructor;

				object returnValue = ReadArgumentFromStream(reader, methodBase, invocation, true,
					methodBase.DeclaringType, otherSideProcessId, interfaceOnlyClient);
				invocation.ReturnValue = returnValue;
				// out or ref arguments on ctors are rare, but not generally forbidden, so we continue here
			}
			else if (invocation is ManualInvocation mi2 && mi2.TargetType != null)
			{
				// This happens if we request a remote instance directly (by interface type)
				object returnValue = ReadArgumentFromStream(reader, mi2.Method, invocation, true, mi2.TargetType,
					otherSideProcessId, interfaceOnlyClient);
				invocation.ReturnValue = returnValue;
				return;
			}
			else
			{
				MethodInfo me = invocation.Method;
				methodBase = me;
				if (me.ReturnType != typeof(void))
				{
					object returnValue = ReadArgumentFromStream(reader, methodBase, invocation, true, me.ReturnType,
						otherSideProcessId, interfaceOnlyClient);
					invocation.ReturnValue = returnValue;
				}
			}

			int index = 0;
			foreach (var byRefArguments in methodBase.GetParameters())
			{
				if (byRefArguments.ParameterType.IsArray && methodBase.DeclaringType != null &&
					methodBase.DeclaringType.IsSubclassOf(typeof(Stream)))
				{
					// Copy the contents of the array-to-be-filled
					object byRefValue = ReadArgumentFromStream(reader, methodBase, invocation, false,
						byRefArguments.ParameterType, otherSideProcessId, interfaceOnlyClient);
					Array source = (Array)byRefValue; // The data from the remote side
					Array destination = ((Array)invocation.Arguments[index]); // The argument to be filled
					if (source.Length != destination.Length)
					{
						throw new RemotingException("Array size mismatch: Return data size inconsistent");
					}

					Array.Copy(source, destination, source.Length);
				}
				else if (byRefArguments.ParameterType.IsByRef)
				{
					object byRefValue = ReadArgumentFromStream(reader, methodBase, invocation, false,
						byRefArguments.ParameterType, otherSideProcessId, interfaceOnlyClient);
					invocation.Arguments[index] = byRefValue;
				}

				index++;
			}
		}

		public object ReadArgumentFromStream(BinaryReader r, MethodBase callingMethod, IInvocation invocation,
			bool canAttemptToInstantiate, Type typeOfArgument, string otherSideProcessId, bool interfaceOnlyClient)
		{
			if (!_initialized)
			{
				throw new RemotingException("Instance is not initialized");
			}

			RemotingReferenceType referenceType = (RemotingReferenceType)r.ReadInt32();
			switch (referenceType)
			{
				case RemotingReferenceType.NullPointer:
					return null;
				case RemotingReferenceType.SerializedItem:
				{
					var formatter = _formatterFactory.CreateOrGetFormatter(otherSideProcessId);
					string typeName = r.ReadString();
					int dataLength = r.ReadInt32();
					Type t = Server.GetTypeFromAnyAssembly(typeName);
					object decodedArg = JsonSerializer.Deserialize(new JsonSplitStream(r.BaseStream, dataLength), t, formatter);

					_formatterFactory.FinalizeDeserialization(r, formatter);

					return decodedArg;
				}

				case RemotingReferenceType.RemoteReference:
				{
					// The server sends a reference to an object that he owns
					string objectId = r.ReadString();
					string typeName = r.ReadString();
					int providedInterfaces = r.ReadInt32();
					List<string> knownInterfaces = null;
					for (int i = 0; i < providedInterfaces; i++)
					{
						if (knownInterfaces == null)
						{
							knownInterfaces = new List<string>();
						}

						knownInterfaces.Add(r.ReadString());
					}

					object instance = null;
					instance = InstanceManager.CreateOrGetProxyForObjectId(invocation, canAttemptToInstantiate,
						typeOfArgument, typeName, objectId, knownInterfaces);
					return instance;
				}

				case RemotingReferenceType.InstanceOfSystemType:
				{
					string typeName = r.ReadString();
					Type t = Server.GetTypeFromAnyAssembly(typeName);
					return t;
				}

				case RemotingReferenceType.ArrayOfSystemType:
				{
					int count = r.ReadInt32();
					Type[] ts = new Type[count];
					for (int i = 0; i < count; i++)
					{
						string typeName = r.ReadString();
						if (!string.IsNullOrEmpty(typeName))
						{
							Type t = Server.GetTypeFromAnyAssembly(typeName);
							ts[i] = t;
						}
						else
						{
							ts[i] = null;
						}
					}

					return ts;
				}

				case RemotingReferenceType.ContainerType:
				{
					string typeName = r.ReadString();
					Type t = Server.GetTypeFromAnyAssembly(typeName);
					Type contentType = Server.GetTypeFromAnyAssembly(r.ReadString());
					IList list = (IList)Activator.CreateInstance(t);
					bool cont = r.ReadBoolean();
					while (cont)
					{
						var nextElem = ReadArgumentFromStream(r, callingMethod, invocation, canAttemptToInstantiate,
							contentType, otherSideProcessId, interfaceOnlyClient);
						list.Add(nextElem);
						cont = r.ReadBoolean();
					}

					return list;
				}

				case RemotingReferenceType.IpAddress:
				{
					string s = r.ReadString();
					return IPAddress.Parse(s);
				}

				case RemotingReferenceType.BoolTrue:
				{
					return true;
				}

				case RemotingReferenceType.BoolFalse:
				{
					return false;
				}

				case RemotingReferenceType.Int32:
				{
					int i = r.ReadInt32();
					return i;
				}

				case RemotingReferenceType.Uint32:
				{
					var i = r.ReadUInt32();
					return i;
				}

				case RemotingReferenceType.Int8:
				{
					var i = r.ReadSByte();
					return i;
				}

				case RemotingReferenceType.Uint8:
				{
					var i = r.ReadByte();
					return i;
				}

				case RemotingReferenceType.Int16:
				{
					var i = r.ReadInt16();
					return i;
				}

				case RemotingReferenceType.Uint16:
				{
					var i = r.ReadUInt16();
					return i;
				}

				case RemotingReferenceType.Int64:
				{
					var i = r.ReadInt64();
					return i;
				}

				case RemotingReferenceType.Uint64:
				{
					var i = r.ReadUInt64();
					return i;
				}

				case RemotingReferenceType.Float:
				{
					var i = r.ReadSingle();
					return i;
				}

				case RemotingReferenceType.Double:
				{
					var i = r.ReadDouble();
					return i;
				}

				case RemotingReferenceType.Half:
				{
					var i = r.ReadHalf();
					return i;
				}

				case RemotingReferenceType.String:
				{
					int numBytesToRead = r.ReadInt32();
					if (numBytesToRead == 0)
					{
						return string.Empty;
					}

					byte[] buffer = ArrayPool<byte>.Shared.Rent(numBytesToRead);
					int numBytesRead = r.Read(buffer, 0, numBytesToRead);
					if (numBytesRead != numBytesToRead)
					{
						throw new RemotingException("Unexpected end of stream or data corruption encountered");
					}

					String ret = _stringEncoding.GetString(buffer, 0, numBytesRead);
					ArrayPool<byte>.Shared.Return(buffer);
					return ret;
				}

				case RemotingReferenceType.ByteArray:
				{
					int numElements = r.ReadInt32();
					if (numElements == 0)
					{
						return Array.Empty<byte>();
					}

					byte[] ret = new byte[numElements];
					int numElementsRead = 0;

					while (numElementsRead < numElements)
					{
						// We eventually need to read really large chunks of data, but Read may return before that if the block is larger than ~1MB.
						numElementsRead += r.Read(ret, numElementsRead, numElements - numElementsRead);
					}

					if (numElementsRead != numElements)
					{
						throw new RemotingException("Unexpected end of stream - Incomplete binary data transmission");
					}

					return ret;
				}

				case RemotingReferenceType.AddEvent:
				{
					lock (ConcurrentOperationsLock)
					{
						string instanceId = r.ReadString();
						bool hasReturnValue = r.ReadBoolean();
						int methodGenericArgs = r.ReadInt32();
						Type[] typeOfGenericArguments = new Type[methodGenericArgs];
						for (int i = 0; i < methodGenericArgs; i++)
						{
							var typeName = r.ReadString();
							var t = Server.GetTypeFromAnyAssembly(typeName);
							typeOfGenericArguments[i] = t;
						}

						// Determine the method to call on the client side.
						Type typeOfTarget = null;
						if (hasReturnValue)
						{
							switch (typeOfGenericArguments.Length)
							{
								case 1:
									typeOfTarget =
										typeof(DelegateFuncProxyOnClient<>).MakeGenericType(typeOfGenericArguments[0]);
									break;
								case 2:
									typeOfTarget =
										typeof(DelegateFuncProxyOnClient<,>).MakeGenericType(typeOfGenericArguments[0],
											typeOfGenericArguments[1]);
									break;
								case 3:
									typeOfTarget =
										typeof(DelegateFuncProxyOnClient<,,>).MakeGenericType(typeOfGenericArguments[0],
											typeOfGenericArguments[1], typeOfGenericArguments[2]);
									break;
								case 4:
									typeOfTarget =
										typeof(DelegateFuncProxyOnClient<,,>).MakeGenericType(typeOfGenericArguments[0],
											typeOfGenericArguments[1], typeOfGenericArguments[2],
											typeOfGenericArguments[3]);
									break;
								case 5:
									typeOfTarget =
										typeof(DelegateFuncProxyOnClient<,,>).MakeGenericType(typeOfGenericArguments[0],
											typeOfGenericArguments[1], typeOfGenericArguments[2], typeOfGenericArguments[3],
											typeOfGenericArguments[4]);
									break;
								default:
									throw new InvalidRemotingOperationException(
										$"Unsupported number of arguments for function ({typeOfGenericArguments.Length}");
							}
						}
						else
						{
							switch (typeOfGenericArguments.Length)
							{
								case 0:
									typeOfTarget =
										typeof(DelegateProxyOnClient);
									break;
								case 1:
									typeOfTarget =
										typeof(DelegateProxyOnClient<>).MakeGenericType(typeOfGenericArguments[0]);
									break;
								case 2:
									typeOfTarget = typeof(DelegateProxyOnClient<,>).MakeGenericType(
										typeOfGenericArguments[0],
										typeOfGenericArguments[1]);
									break;
								case 3:
									typeOfTarget = typeof(DelegateProxyOnClient<,>).MakeGenericType(
										typeOfGenericArguments[0],
										typeOfGenericArguments[1], typeOfGenericArguments[2]);
									break;
								case 4:
									typeOfTarget = typeof(DelegateProxyOnClient<,>).MakeGenericType(
										typeOfGenericArguments[0],
										typeOfGenericArguments[1], typeOfGenericArguments[2], typeOfGenericArguments[3]);
									break;
								default:
									throw new InvalidRemotingOperationException(
										$"Unsupported number of arguments for action ({typeOfGenericArguments.Length}");
							}
						}

						var methodInfoOfTarget = typeOfTarget.GetMethod("FireEvent",
							BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

						// This creates an instance of the DelegateInternalSink class, which acts as a proxy for delegate callbacks. Instead of the actual delegate
						// target, we register a method from this class as a delegate target
						DelegateInternalSink internalSink;
						if (_instanceManager.TryGetObjectFromId(instanceId, out object sink))
						{
							internalSink = (DelegateInternalSink)sink;
							// throw new InvalidRemotingOperationException($"Instance id {instanceId} already has a DelegateInternalSink");
						}
						else
						{
							var interceptor = InstanceManager.GetInterceptor(_interceptors, instanceId);
							internalSink = new DelegateInternalSink(interceptor, instanceId, methodInfoOfTarget);
							var usedInstance = _instanceManager.AddInstance(internalSink, instanceId, interceptor.OtherSideProcessId,
								internalSink.GetType(), internalSink.GetType().AssemblyQualifiedName, false);

							internalSink = (DelegateInternalSink)usedInstance;
						}

						internalSink.RegisterInstance(otherSideProcessId);

						// TODO: This copying of arrays here is not really performance-friendly
						var argumentsOfTarget = methodInfoOfTarget.GetParameters().Select(x => x.ParameterType).ToList();

						IEnumerable<MethodInfo> possibleSinks = null;

						MethodInfo localSinkTarget;
						if (methodInfoOfTarget.ReturnType == typeof(void))
						{
							possibleSinks = internalSink.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
								.Where(x => x.Name == "ActionSink");
							localSinkTarget =
								possibleSinks.Single(x => x.GetGenericArguments().Length == argumentsOfTarget.Count);
						}
						else
						{
							possibleSinks = internalSink.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
								.Where(x => x.Name == "FuncSink");
							localSinkTarget = possibleSinks.Single(x =>
								x.GetGenericArguments().Length == argumentsOfTarget.Count + 1);
							argumentsOfTarget.Add(methodInfoOfTarget.ReturnType);
						}

						if (argumentsOfTarget.Count > 0)
						{
							localSinkTarget = localSinkTarget.MakeGenericMethod(argumentsOfTarget.ToArray());
						}

						// create the local server side delegate
						Delegate newDelegate = Delegate.CreateDelegate(typeOfArgument, internalSink, localSinkTarget);
						string delegateId = _instanceManager.GetDelegateTargetIdentifier(newDelegate, otherSideProcessId);
						var actualInstance = _instanceManager.AddInstance(newDelegate, delegateId, otherSideProcessId, newDelegate.GetType(), newDelegate.GetType().AssemblyQualifiedName, false);
						var actualDelegate = (Delegate)actualInstance;
						if (ReferenceEquals(actualDelegate, newDelegate))
						{
							if (internalSink.TheActualDelegate != null)
							{
								throw new InvalidRemotingOperationException("Expecting actual delegate to not exist here");
							}

							internalSink.TheActualDelegate = newDelegate;
							return newDelegate;
						}

						return actualDelegate;
					}
				}

				case RemotingReferenceType.RemoveEvent:
				{
					string instanceId = r.ReadString();
					lock (ConcurrentOperationsLock)
					{
						if (_instanceManager.TryGetObjectFromId(instanceId, out var internalSink))
						{
							DelegateInternalSink sink = (DelegateInternalSink)internalSink;
							if (sink.Unregister(otherSideProcessId))
							{
								_instanceManager.Remove(instanceId, otherSideProcessId, true);
								var del = sink.TheActualDelegate;
								sink.TheActualDelegate = null; // Is not registered any more

								// argument required to deregister the sink from the target, but nothing happens if it's null, because we use a wrong path later on
								// This is only null in exceptional cases ("CanFireEventWhileDisconnecting" test)
								return del;
							}
						}

					}

					return null;
				}

				case RemotingReferenceType.MethodPointer:
				{
					string instanceId = r.ReadString();
					string typeOfTargetName = r.ReadString();
					int tokenOfTargetMethod = r.ReadInt32();
					int methodGenericArgs = r.ReadInt32();
					Type[] typeOfGenericArguments = new Type[methodGenericArgs];
					for (int i = 0; i < methodGenericArgs; i++)
					{
						var typeName = r.ReadString();
						var t = Server.GetTypeFromAnyAssembly(typeName);
						typeOfGenericArguments[i] = t;
					}

					Type typeOfTarget = Server.GetTypeFromAnyAssembly(typeOfTargetName);

					var methods = typeOfTarget.GetMethods(BindingFlags.Instance | BindingFlags.Static |
														  BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo methodInfoOfTarget = methods.First(x => x.MetadataToken == tokenOfTargetMethod);
					if (methodGenericArgs > 0)
					{
						methodInfoOfTarget = methodInfoOfTarget.MakeGenericMethod(typeOfGenericArguments);
					}

					// TODO: This copying of arrays here is not really performance-friendly
					var argumentsOfTarget = methodInfoOfTarget.GetParameters().Select(x => x.ParameterType).ToList();

					var interceptor = InstanceManager.GetInterceptor(_interceptors, instanceId);
					// This creates an instance of the DelegateInternalSink class, which acts as a proxy for delegate callbacks. Instead of the actual delegate
					// target, we register a method from this class as a delegate target
					DelegateInternalSink internalSink;
					if (_instanceManager.TryGetObjectFromId(instanceId, out object sink))
					{
						internalSink = (DelegateInternalSink)sink;
					}
					else
					{
						internalSink = new DelegateInternalSink(interceptor, instanceId, methodInfoOfTarget);
						var usedInstance = _instanceManager.AddInstance(internalSink, instanceId, interceptor.OtherSideProcessId,
							internalSink.GetType(), internalSink.GetType().AssemblyQualifiedName, false);
						internalSink = (DelegateInternalSink)usedInstance;
					}

					IEnumerable<MethodInfo> possibleSinks = null;
					MethodInfo localSinkTarget;
					if (methodInfoOfTarget.ReturnType == typeof(void))
					{
						possibleSinks = internalSink.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
							.Where(x => x.Name == "ActionSink");
						localSinkTarget =
							possibleSinks.Single(x => x.GetGenericArguments().Length == argumentsOfTarget.Count);
					}
					else
					{
						possibleSinks = internalSink.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
							.Where(x => x.Name == "FuncSink");
						localSinkTarget = possibleSinks.Single(x =>
							x.GetGenericArguments().Length == argumentsOfTarget.Count + 1);
						argumentsOfTarget.Add(methodInfoOfTarget.ReturnType);
					}

					if (argumentsOfTarget.Count > 0)
					{
						localSinkTarget = localSinkTarget.MakeGenericMethod(argumentsOfTarget.ToArray());
					}

					// No need to register - this is a delegate used as method argument in an "ordinary" call
					var newDelegate = Delegate.CreateDelegate(typeOfArgument, internalSink, localSinkTarget);
					return newDelegate;
				}

				default:
					throw new InvalidRemotingOperationException("Unknown argument type");
			}
		}

		public void SendExceptionReply(Exception exception, BinaryWriter w, int sequence, string otherSideProcessId)
		{
			RemotingCallHeader hdReturnValue = new RemotingCallHeader(RemotingFunctionType.ExceptionReturn, sequence);
			using var lck = hdReturnValue.WriteHeader(w);
			// We're manually serializing exceptions, because that's apparently how this should be done (since
			// ExceptionDispatchInfo.SetRemoteStackTrace doesn't work if the stack trace has already been set)
			EncodeException(exception, w);
			while (exception.InnerException != null)
			{
				// True: Another one follows
				w.Write(true);
				exception = exception.InnerException;
				EncodeException(exception, w);
			}

			w.Write(false);
		}

		private static void EncodeException(Exception exception, BinaryWriter w)
		{
			w.Write(exception.GetType().AssemblyQualifiedName ?? string.Empty);
			w.Write(exception.Message);
			w.Write(exception.HResult);
			w.Write(exception.StackTrace ?? string.Empty);
		}

		public Exception DecodeException(BinaryReader reader, string otherSideProcessId)
		{
			FieldInfo[] fields = typeof(Exception).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
			var field = fields.FirstOrDefault(x => x.Name == "_innerException");
			Exception root = DecodeExceptionInternal(reader, otherSideProcessId);
			Exception current = root;
			while (true)
			{
				bool more = reader.ReadBoolean();
				if (more == false)
				{
					break;
				}

				Exception sub = DecodeExceptionInternal(reader, otherSideProcessId);

				// This field is normally read-only
				if (field != null)
				{
					field.SetValue(current, sub);
				}

				current = sub; // Even if the above fails, we need to make sure we read all elements.
			}

			return root;
		}

		private Exception DecodeExceptionInternal(BinaryReader reader, string otherSideProcessId)
		{
			string exceptionTypeName = reader.ReadString();
			string remoteMessage = reader.ReadString();
			int hresult = reader.ReadInt32();
			string remoteStack = reader.ReadString();
			Type exceptionType = Server.GetTypeFromAnyAssembly(exceptionTypeName);

			var ctors = exceptionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			// Prefer ctors with many arguments
			ctors = ctors.OrderByDescending(x => x.GetParameters().Length).ToArray();

			// Manually search the deserialization constructor. Activator.CreateInstance is not helpful when something is wrong
			Exception decodedException = null;
			foreach (var ctor in ctors)
			{
				var parameters = ctor.GetParameters();
				if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(Exception))
				{
					decodedException = (Exception)ctor.Invoke(new object[] { remoteMessage, null });
					break;
				}

				if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
				{
					decodedException = (Exception)ctor.Invoke(new object[] { remoteMessage });
					break;
				}

				if (parameters.Length == 0)
				{
					decodedException = (Exception)ctor.Invoke(Array.Empty<Object>());
					break;
				}
			}

			if (decodedException == null)
			{
				// We have neither a default ctor nor a deserialization ctor. This is bad.
				decodedException = new RemotingException($"Unable to deserialize exception of type {exceptionTypeName}. Please fix it's deserialization constructor");
			}

			decodedException.HResult = hresult;
			try
			{
				ExceptionDispatchInfo.SetRemoteStackTrace(decodedException!, remoteStack);
			}
			catch (InvalidOperationException)
			{
				throw new RemotingException(
					$"Unable to properly deserialize exception {decodedException}. Inner exception is {decodedException.Message}",
					decodedException);
			}

			return decodedException;
		}

		public void AddInterceptor(ClientSideInterceptor newInterceptor)
		{
			_interceptors.Add(newInterceptor.OtherSideProcessId, newInterceptor);
			_initialized = true; // at least one entry exists
		}
	}
}
