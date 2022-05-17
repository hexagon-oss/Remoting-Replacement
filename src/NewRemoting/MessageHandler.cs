using System;
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
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;

namespace NewRemoting
{
	/// <summary>
	/// This class is responsible for encoding and decoding remote calls.
	/// It encodes the arguments when invoking a remote method and decodes them again on the server side.
	/// </summary>
	internal class MessageHandler
	{
		private readonly InstanceManager _instanceManager;
		private readonly FormatterFactory _formatterFactory;
		private readonly object _communicationLock;
		private readonly Dictionary<string, ClientSideInterceptor> _interceptors;
		private bool _initialized;

		public MessageHandler(InstanceManager instanceManager, FormatterFactory formatterFactory)
		{
			_instanceManager = instanceManager;
			_formatterFactory = formatterFactory;
			_communicationLock = new object();
			_initialized = false;
			_interceptors = new();
		}

		/// <summary>
		/// Exposing this is a bit dangerous, but since everything is internal, it should be fine
		/// </summary>
		internal object CommunicationLinkLock => _communicationLock;

		public InstanceManager InstanceManager => _instanceManager;

		public static bool HasDefaultCtor(Type t)
		{
			var ctor = t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, CallingConventions.Any, new Type[0], null);
			return ctor != null;
		}

		/// <summary>
		/// Write the given object to the target stream.
		/// When it is a type that shall be transferred by value, it is serialized, otherwise a reference is added to the stream.
		/// That reference is then converted to a proxy instance on the other side.
		/// </summary>
		/// <param name="w">The data sink</param>
		/// <param name="data">The object to write</param>
		/// <param name="referencesWillBeSentTo">Destination identifier (used to keep track of references that are eventually encoded in the stream)</param>
		public void WriteArgumentToStream(BinaryWriter w, object data, string referencesWillBeSentTo)
		{
			if (!_initialized)
			{
				throw new InvalidOperationException("Instance is not initialized");
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
				w.Write((int)RemotingReferenceType.InstanceOfSystemType);
				w.Write(type.AssemblyQualifiedName);
			}
			else if (data is IPAddress address)
			{
				// IPAddress is not serializable, even though it is actually trivially-serializable
				w.Write((int)RemotingReferenceType.IpAddress);
				string s = address.ToString();
				w.Write(s);
			}
			else if (data is Type[] typeArray)
			{
				w.Write((int)RemotingReferenceType.ArrayOfSystemType);
				w.Write(typeArray.Length);
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
				w.Write(data.GetType().AssemblyQualifiedName);
				w.Write(contentType.AssemblyQualifiedName);
				foreach (object obj in list)
				{
					// Recursively write the arguments
					w.Write(true);
					WriteArgumentToStream(w, obj, referencesWillBeSentTo);
				}

				w.Write(false); // Terminate the array
			}
			else if (data is Delegate del)
			{
				if (del.Method.IsStatic)
				{
					throw new RemotingException("Can only register instance methods as delegate targets");
				}

				// The argument is a function pointer (typically the argument to a add_ or remove_ event)
				w.Write((int)RemotingReferenceType.MethodPointer);
				if (del.Target != null)
				{
					string instanceId = _instanceManager.GetMethodInfoIdentifier(del.Method);
					_instanceManager.AddInstance(del, instanceId, referencesWillBeSentTo, del.GetType());
					w.Write(instanceId);
				}
				else
				{
					// The delegate target is a static method
					w.Write(string.Empty);
				}

				string targetId = _instanceManager.GetIdForObject(del, referencesWillBeSentTo);
				w.Write(targetId);
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
						throw new RemotingException("Unresolved generic type or some other undefined case");
					}

					w.Write(arg);
				}
			}
			else if (Client.IsRemoteProxy(data))
			{
				// Proxies are never serializable
				if (!_instanceManager.TryGetObjectId(data, out string objectId, out Type originalType))
				{
					throw new RemotingException("A proxy has no existing reference");
				}

				w.Write((int)RemotingReferenceType.RemoteReference);
				w.Write(objectId);
				// string originalTypeName = Client.GetUnproxiedType(data).AssemblyQualifiedName ?? String.Empty;
				string originalTypeName = originalType.AssemblyQualifiedName ?? string.Empty;
				w.Write(originalTypeName);
			}
			else if (t.IsSerializable)
			{
				SendSerializedObject(w, data, referencesWillBeSentTo);
			}
			else if (t.IsAssignableTo(typeof(MarshalByRefObject)))
			{
				string objectId = _instanceManager.GetIdForObject(data, referencesWillBeSentTo);
				w.Write((int)RemotingReferenceType.RemoteReference);
				w.Write(objectId);

				// If this is not a proxy, this should always work correctly
				var assemblyQualitfiedTypeName = Client.GetUnproxiedType(data).AssemblyQualifiedName ?? String.Empty;
				w.Write(assemblyQualitfiedTypeName);
			}
			else
			{
				throw new SerializationException($"Object {data} is neither serializable nor MarshalByRefObject");
			}
		}

		private void SendSerializedObject(BinaryWriter w, object data, string otherSideInstanceId)
		{
			MemoryStream ms = new MemoryStream();

#pragma warning disable 618
			var formatter = _formatterFactory.CreateOrGetFormatter(otherSideInstanceId);
			formatter.Serialize(ms, data);
#pragma warning restore 618
			w.Write((int)RemotingReferenceType.SerializedItem);
			w.Write((int)ms.Length);
			var array = ms.ToArray();

			/*
			// The following is for testing purposes only (slow!)
			// It tests that the serialized code doesn't contain any serialized proxies. This has false positives when
			// copying files from/to a remote endpoint, because dlls may contain the requested string.
			byte[] compare = Encoding.ASCII.GetBytes("DynamicProxyGenAss");
			for (int i = 0; i < array.Length; i++)
			{
				int needleIdx = 0;
				while (needleIdx < compare.Length && array[i + needleIdx] == compare[needleIdx])
				{
					needleIdx++;
				}

				if (needleIdx >= compare.Length)
				{
					ms.Position = 0;
#pragma warning disable 618
					_formatter.Serialize(ms, data);
#pragma warning restore 618
					throw new RemotingException("Should not have serialized a dynamic proxy with its internal name");
				}
			}
			*/

			w.Write(array, 0, (int)ms.Length);
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

					if (type.IsSerializable)
					{
						return false;
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
				else if (paramType.IsArray && paramType.GetElementType().IsValueType && paramType.GetElementType().IsPrimitive)
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

		public void ProcessCallResponse(IInvocation invocation, BinaryReader reader, string otherSideInstanceId)
		{
			if (!_initialized)
			{
				throw new InvalidOperationException("Instance is not initialized");
			}

			MethodBase methodBase;
			// This is true if this is a reply to a CreateInstance call (invocation.Method cannot be a ConstructorInfo instance)
			if (invocation is ManualInvocation mi && mi.Method == null && mi.Constructor != null)
			{
				methodBase = mi.Constructor;

				object returnValue = ReadArgumentFromStream(reader, methodBase, invocation, true, methodBase.DeclaringType, otherSideInstanceId);
				invocation.ReturnValue = returnValue;
				// out or ref arguments on ctors are rare, but not generally forbidden, so we continue here
			}
			else if (invocation is ManualInvocation mi2 && mi2.TargetType != null)
			{
				// This happens if we request a remote instance directly (by interface type)
				object returnValue = ReadArgumentFromStream(reader, mi2.Method, invocation, true, mi2.TargetType, otherSideInstanceId);
				invocation.ReturnValue = returnValue;
				return;
			}
			else
			{
				MethodInfo me = invocation.Method;
				methodBase = me;
				if (me.ReturnType != typeof(void))
				{
					object returnValue = ReadArgumentFromStream(reader, methodBase, invocation, true, me.ReturnType, otherSideInstanceId);
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
					object byRefValue = ReadArgumentFromStream(reader, methodBase, invocation, false, byRefArguments.ParameterType, otherSideInstanceId);
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
					object byRefValue = ReadArgumentFromStream(reader, methodBase, invocation, false, byRefArguments.ParameterType, otherSideInstanceId);
					invocation.Arguments[index] = byRefValue;
				}

				index++;
			}
		}

		public void SendExceptionReply(Exception exception, BinaryWriter w, int sequence, string otherSideInstanceId)
		{
			RemotingCallHeader hdReturnValue = new RemotingCallHeader(RemotingFunctionType.ExceptionReturn, sequence);
			hdReturnValue.WriteTo(w);
			// We're manually serializing exceptions, because that's apparently how this should be done (since
			// ExceptionDispatchInfo.SetRemoteStackTrace doesn't work if the stack trace has already been set)
			w.Write(exception.GetType().AssemblyQualifiedName ?? string.Empty);
			SerializationInfo info = new SerializationInfo(exception.GetType(), new FormatterConverter());
			StreamingContext ctx = new StreamingContext();
			exception.GetObjectData(info, ctx);
			w.Write(info.MemberCount);
			foreach (var e in info)
			{
				w.Write(e.Name);
				// This may contain inner exceptions, but since we're not throwing those, this shouldn't cause any issues on the remote side
				WriteArgumentToStream(w, e.Value, otherSideInstanceId);
			}

			w.Write(exception.StackTrace ?? string.Empty);
		}

		public object ReadArgumentFromStream(BinaryReader r, MethodBase callingMethod, IInvocation invocation, bool canAttemptToInstantiate, Type typeOfArgument, string otherSideInstanceId)
		{
			if (!_initialized)
			{
				throw new InvalidOperationException("Instance is not initialized");
			}

			RemotingReferenceType referenceType = (RemotingReferenceType)r.ReadInt32();
			switch (referenceType)
			{
				case RemotingReferenceType.NullPointer:
					return null;
				case RemotingReferenceType.SerializedItem:
				{
					int argumentLen = r.ReadInt32();
					byte[] argumentData = r.ReadBytes(argumentLen);
					MemoryStream ms = new MemoryStream(argumentData, false);
#pragma warning disable 618
					var formatter = _formatterFactory.CreateOrGetFormatter(otherSideInstanceId);
					object decodedArg = formatter.Deserialize(ms);
#pragma warning restore 618
					return decodedArg;
				}

				case RemotingReferenceType.RemoteReference:
				{
					// The server sends a reference to an object that he owns
					string objectId = r.ReadString();
					string typeName = r.ReadString();
					object instance = null;
					instance = InstanceManager.CreateOrGetProxyForObjectId(invocation, canAttemptToInstantiate, typeOfArgument, typeName, objectId);
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
						var nextElem = ReadArgumentFromStream(r, callingMethod, invocation, canAttemptToInstantiate, contentType, otherSideInstanceId);
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

				case RemotingReferenceType.MethodPointer:
				{
					string instanceId = r.ReadString();
					string targetId = r.ReadString();
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

					var methods = typeOfTarget.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
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
						_instanceManager.AddInstance(internalSink, instanceId, interceptor.OtherSideInstanceId, internalSink.GetType());
					}

					IEnumerable<MethodInfo> possibleSinks = null;

					MethodInfo localSinkTarget;
					if (methodInfoOfTarget.ReturnType == typeof(void))
					{
						possibleSinks = internalSink.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(x => x.Name == "ActionSink");
						localSinkTarget = possibleSinks.Single(x => x.GetGenericArguments().Length == argumentsOfTarget.Count);
					}
					else
					{
						possibleSinks = internalSink.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(x => x.Name == "FuncSink");
						localSinkTarget = possibleSinks.Single(x => x.GetGenericArguments().Length == argumentsOfTarget.Count + 1);
						argumentsOfTarget.Add(methodInfoOfTarget.ReturnType);
					}

					if (argumentsOfTarget.Count > 0)
					{
						localSinkTarget = localSinkTarget.MakeGenericMethod(argumentsOfTarget.ToArray());
					}

					string delegateId = instanceId + "." + methodInfoOfTarget.Name;
					Delegate newDelegate;
					if (callingMethod != null && callingMethod.IsSpecialName && callingMethod.Name.StartsWith("add_", StringComparison.Ordinal))
					{
						newDelegate = Delegate.CreateDelegate(typeOfArgument, internalSink, localSinkTarget);
						_instanceManager.AddInstance(newDelegate, delegateId, otherSideInstanceId, newDelegate.GetType());
						return newDelegate;
					}
					else if (callingMethod != null && callingMethod.IsSpecialName && callingMethod.Name.StartsWith("remove_", StringComparison.Ordinal))
					{
						// This may fail if the delegate was already removed. In this case, we just return a new instance, it will be GCed soon after
						if (_instanceManager.TryGetObjectFromId(delegateId, out var obj))
						{
							// Remove delegate (and forget about it), it is not used here any more.
							_instanceManager.Remove(delegateId, otherSideInstanceId);
							return obj;
						}
					}

					// No need to register - this is a delegate used as method argument in an "ordinary" call
					newDelegate = Delegate.CreateDelegate(typeOfArgument, internalSink, localSinkTarget);
					return newDelegate;
				}

				default:
					throw new RemotingException("Unknown argument type");
			}
		}

		public Exception DecodeException(BinaryReader reader, string otherSideInstanceId)
		{
			string exceptionTypeName = reader.ReadString();
			Dictionary<string, object> stringValuePairs = new Dictionary<string, object>();
			int numValues = reader.ReadInt32();
			for (int i = 0; i < numValues; i++)
			{
				string name = reader.ReadString();
				// The values found here should all be serializable again
				object data = ReadArgumentFromStream(reader, null, null, false, typeof(object),
					otherSideInstanceId);
				stringValuePairs.Add(name, data);
			}

			string remoteStack = reader.ReadString();
			Type exceptionType = Server.GetTypeFromAnyAssembly(exceptionTypeName);
			SerializationInfo info = new SerializationInfo(exceptionType, new FormatterConverter());

			foreach (var e in stringValuePairs)
			{
				switch (e.Key)
				{
					case "StackTraceString":
						// We don't want to set this one, so we are able to use SetRemoteStackTrace
						info.AddValue(e.Key, null);
						break;
					case "RemoteStackTraceString":
						// If this is already provided, the exception has probably been thrown on yet another instance
						info.AddValue(e.Key, null);
						remoteStack += " previously thrown at " + e.Value;
						break;
					default:
						info.AddValue(e.Key, e.Value);
						break;
				}
			}

			StreamingContext ctx = new StreamingContext();
			var ctors = exceptionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

			// Default ctor as fallback
			ConstructorInfo ctorToUse = null;

			// Manually search the deserialization constructor. Activator.CreateInstance is not helpful when something is wrong
			Exception decodedException = null;
			foreach (var ctor in ctors)
			{
				var parameters = ctor.GetParameters();
				if (parameters.Length == 2 && parameters[0].ParameterType == typeof(SerializationInfo) && parameters[1].ParameterType == typeof(StreamingContext))
				{
					ctorToUse = ctor;
					decodedException = (Exception)ctorToUse.Invoke(new object[] { info, ctx });
					break;
				}
			}

			if (decodedException == null)
			{
				ctorToUse = ctors.FirstOrDefault(x => x.GetParameters().Length == 0);
				if (ctorToUse != null)
				{
					decodedException = (Exception)ctorToUse.Invoke(Array.Empty<object>());
				}
			}

			if (decodedException == null)
			{
				// We have neither a default ctor nor a deserialization ctor. This is bad.
				decodedException = new RemotingException($"Unable to deserialize exception of type {exceptionTypeName}. Please fix it's deserialization constructor");
			}

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
			_interceptors.Add(newInterceptor.OtherSideInstanceId, newInterceptor);
			_initialized = true; // at least one entry exists
		}
	}
}
