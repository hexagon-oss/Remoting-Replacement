using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;

namespace NewRemoting
{
	internal class MessageHandler
	{
		private readonly InstanceManager _instanceManager;
		private readonly IFormatter _formatter;
		private readonly ProxyGenerator _proxyGenerator;
		private readonly object _communicationLock;
		private bool _initialized;

		public MessageHandler(InstanceManager instanceManager, ProxyGenerator proxyGenerator, IFormatter formatter)
		{
			_instanceManager = instanceManager;
			_formatter = formatter;
			_proxyGenerator = proxyGenerator;
			_communicationLock = new object();
			_initialized = false;
		}

		/// <summary>
		/// Initializes this instance. Separate because of circular dependencies. 
		/// </summary>
		public void Init(IInterceptor interceptor)
		{
			Interceptor = interceptor;
			_initialized = true;
		}

		public IInterceptor Interceptor
		{
			get;
			private set;
		}

		/// <summary>
		/// Exposing this is a bit dangerous, but since everything is internal, it should be fine
		/// </summary>
		internal object CommunicationLinkLock => _communicationLock;

		public InstanceManager InstanceManager => _instanceManager;

		public void WriteArgumentToStream(BinaryWriter w, object data)
		{
			if (!_initialized)
			{
				throw new InvalidOperationException("Instance is not initialized");
			}
			MemoryStream ms = new MemoryStream();
			if (ReferenceEquals(data, null))
			{
				w.Write((int)RemotingReferenceType.NullPointer);
				return;
			}
			Type t = data.GetType();
			if (data is Type type)
			{
				w.Write((int)RemotingReferenceType.InstanceOfSystemType);
				w.Write(type.AssemblyQualifiedName);
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
					WriteArgumentToStream(w, obj);
				}
				w.Write(false); // Terminate the array
			}
			else if (data is Delegate del)
			{
				if (!del.Method.IsPublic)
				{
					throw new RemotingException("Delegate target methods that are used in remoting must be public", RemotingExceptionKind.UnsupportedOperation);
				}

				if (del.Method.IsStatic)
				{
					throw new RemotingException("Can only register instance methods as delegate targets", RemotingExceptionKind.UnsupportedOperation);
				}

				// The argument is a function pointer (typically the argument to a add_ or remove_ event)
				w.Write((int)RemotingReferenceType.MethodPointer);
				if (del.Target != null)
				{
					string instanceId = _instanceManager.GetIdForObject(del.Target);
					w.Write(instanceId);
				}
				else
				{
					// The delegate target is a static method
					w.Write(string.Empty);
				}

				string targetId = _instanceManager.GetIdForObject(del);
				w.Write(targetId);
				w.Write(del.Method.DeclaringType.AssemblyQualifiedName);
				w.Write(del.Method.MetadataToken);
			}
			else if (Client.IsRemoteProxy(data))
            {
				// Proxies are never serializable
				if (!_instanceManager.TryGetObjectId(data, out string objectId))
				{
					throw new RemotingException("A proxy has no existing reference", RemotingExceptionKind.ProxyManagementError);
				}
                w.Write((int)RemotingReferenceType.RemoteReference);
                w.Write(objectId);
                w.Write(string.Empty);
			}
			else if (t.IsSerializable)
			{
#pragma warning disable 618
				_formatter.Serialize(ms, data);
#pragma warning restore 618
				w.Write((int)RemotingReferenceType.SerializedItem);
				w.Write((int)ms.Length);
				w.Write(ms.ToArray(), 0, (int)ms.Length);
			}
			else if (t.IsAssignableTo(typeof(MarshalByRefObject)))
			{
				string objectId = _instanceManager.GetIdForObject(data);
				w.Write((int)RemotingReferenceType.RemoteReference);
				w.Write(objectId);
				w.Write(data.GetType().AssemblyQualifiedName);
			}
			else
			{
				throw new SerializationException($"Object {data} is neither serializable nor MarshalByRefObject");
			}
		}

		/// <summary>
		/// True when this type implements <see cref="IList{T}" /> with T being <see cref="MarshalByRefObject"/>. 
		/// </summary>
		private bool TypeIsContainerWithReference(object data, out Type type)
		{
			if (data is IList enumerable)
			{
				var args = enumerable.GetType().GenericTypeArguments;
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
				if (e != null && e.GetType().IsSubclassOf(typeof(MarshalByRefObject)))
				{
					return true;
				}
			}

			return false;
		}

		public void ProcessCallResponse(IInvocation invocation, BinaryReader reader)
		{
			if (!_initialized)
			{
				throw new InvalidOperationException("Instance is not initialized");
			}
			MethodBase methodBase;
			// This is true if this is a reply to a CreateInstance call (invocation.Method cannot be a ConstructorInfo instance)
			if (invocation is ManualInvocation mi && invocation.Method == null)
			{
				methodBase = mi.Constructor;
				if (mi.Constructor == null)
				{
					throw new RemotingException("Unexpected invocation type", RemotingExceptionKind.ProtocolError);
				}
				object returnValue = ReadArgumentFromStream(reader, invocation, true, methodBase.DeclaringType);
				invocation.ReturnValue = returnValue;
				// out or ref arguments on ctors are rare, but not generally forbidden, so we continue here
			}
			else
			{
				MethodInfo me = invocation.Method;
				methodBase = me;
				if (me.ReturnType != typeof(void))
				{
					object returnValue = ReadArgumentFromStream(reader, invocation, true, me.ReturnType);
					invocation.ReturnValue = returnValue;
				}
			}

			int index = 0;
			foreach (var byRefArguments in methodBase.GetParameters())
			{
				if (byRefArguments.ParameterType.IsByRef)
				{
					object byRefValue = ReadArgumentFromStream(reader, invocation, false, byRefArguments.ParameterType);
					invocation.Arguments[index] = byRefValue;
				}

				index++;
			}
		}

		public object ReadArgumentFromStream(BinaryReader r, IInvocation invocation, bool canAttemptToInstantiate, Type typeOfArgument)
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
					object decodedArg = _formatter.Deserialize(ms);
#pragma warning restore 618
					return decodedArg;
				}
				case RemotingReferenceType.RemoteReference:
				{
					// The server sends a reference to an object that he owns
					string objectId = r.ReadString();
					string typeName = r.ReadString();
					object instance = null;
					Type type = string.IsNullOrEmpty(typeName) ? null : Server.GetTypeFromAnyAssembly(typeName);
					switch (type)
					{
						case null:
							// The type name may be omitted if the client knows that this instance must exist
							// (i.e. because it is sending a reference to a proxy back)
							if (_instanceManager.TryGetObjectFromId(objectId, out instance))
							{
								return instance;
							}
							throw new RemotingException("Unknown type found in argument stream", RemotingExceptionKind.ProxyManagementError);
					}

					if (_instanceManager.TryGetObjectFromId(objectId, out instance))
					{
						return instance;
					}

					if (_instanceManager.IsLocalInstanceId(objectId))
					{
						throw new InvalidOperationException("Got an instance that should be local but it isn't");
					}

					// Create a class proxy with all interfaces proxied as well.
					var interfaces = type.GetInterfaces();
					if (typeOfArgument != null && typeOfArgument.IsInterface)
					{
						// If the call returns an interface, only create an interface proxy, because we might not be able to instantiate the actual class (because it's not public, it's sealed, has no public ctors, etc)
						instance = _proxyGenerator.CreateInterfaceProxyWithoutTarget(typeOfArgument, interfaces, Interceptor);
					}
					else if (canAttemptToInstantiate)
					{
						instance = _proxyGenerator.CreateClassProxy(type, interfaces, ProxyGenerationOptions.Default, invocation.Arguments, Interceptor);
					}
					else if ((type.IsSealed || !HasDefaultCtor(type)) && interfaces.Length > 0)
					{
						// Best would be to create a class proxy but we can't. So try an interface proxy with one of the interfaces instead
						instance = _proxyGenerator.CreateInterfaceProxyWithoutTarget(interfaces[0], interfaces, Interceptor);
					}
					else
					{
						instance = _proxyGenerator.CreateClassProxy(type, interfaces, Interceptor);
					}

					_instanceManager.AddInstance(instance, objectId);
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
						var nextElem = ReadArgumentFromStream(r, invocation, canAttemptToInstantiate, contentType);
						list.Add(nextElem);
						cont = r.ReadBoolean();
					}

					return list;
				}
				case RemotingReferenceType.MethodPointer:
				{
					string instanceId = r.ReadString();
					string targetId = r.ReadString();
					string typeOfTargetName = r.ReadString();
					int tokenOfTargetMethod = r.ReadInt32();
					Type typeOfTarget = Server.GetTypeFromAnyAssembly(typeOfTargetName);

					var methods = typeOfTarget.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);
					MethodInfo methodInfoOfTarget = methods.First(x => x.MetadataToken == tokenOfTargetMethod);

					var argumentsOfTarget = methodInfoOfTarget.GetParameters();
					// This creates an instance of the DelegateInternalSink class, which acts as a proxy for delegate callbacks. Instead of the actual delegate
					// target, we register a method from this class as a delegate target
					var internalSink = new DelegateInternalSink(Interceptor, targetId, methodInfoOfTarget);
					_instanceManager.AddInstance(internalSink, targetId);

					var possibleSinks = internalSink.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(x => x.Name == "ActionSink");
					MethodInfo localSinkTarget = possibleSinks.Single(x => x.GetGenericArguments().Length == argumentsOfTarget.Length);
					switch (argumentsOfTarget.Length)
					{
						case > 0:
							localSinkTarget = localSinkTarget.MakeGenericMethod(argumentsOfTarget.Select(x => x.ParameterType).ToArray());
							break;
					}

					return Delegate.CreateDelegate(typeOfArgument, internalSink, localSinkTarget);
				}
				default:
					throw new RemotingException("Unknown argument type", RemotingExceptionKind.UnsupportedOperation);
			}
		}

		public static bool HasDefaultCtor(Type t)
		{
			var ctor = t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, CallingConventions.Any, new Type[0], null);
			return ctor != null;
		}
	}
}
