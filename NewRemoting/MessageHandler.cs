using System;
using System.Collections.Generic;
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

		public MessageHandler(InstanceManager instanceManager, ProxyGenerator proxyGenerator, IFormatter formatter)
		{
			_instanceManager = instanceManager;
			_formatter = formatter;
			_proxyGenerator = proxyGenerator;
		}

		/// <summary>
		/// Interceptor to use for proxies. Public, because of circular ctor dependencies.
		/// </summary>
		public IInterceptor Interceptor
		{
			get;
			internal set;
		}

		public void WriteArgumentToStream(BinaryWriter w, object data)
		{
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

		public void ProcessCallResponse(IInvocation invocation, BinaryReader reader)
		{
			MethodBase methodBase;
			// This is true if this is a reply to a CreateInstance call (invocation.Method cannot be a ConstructorInfo instance)
			if (invocation is ManualInvocation mi && invocation.Method == null)
			{
				methodBase = mi.Constructor;
				if (mi.Constructor == null)
				{
					throw new RemotingException("Unexpected invocation type", RemotingExceptionKind.ProtocolError);
				}
				object returnValue = ReadArgumentFromStream(reader, invocation, true);
				invocation.ReturnValue = returnValue;
				// out or ref arguments on ctors are rare, but not generally forbidden, so we continue here
			}
			else
			{
				MethodInfo me = invocation.Method;
				methodBase = me;
				if (me.ReturnType != typeof(void))
				{
					object returnValue = ReadArgumentFromStream(reader, invocation, true);
					invocation.ReturnValue = returnValue;
				}
			}

			int index = 0;
			foreach (var byRefArguments in methodBase.GetParameters())
			{
				if (byRefArguments.ParameterType.IsByRef)
				{
					object byRefValue = ReadArgumentFromStream(reader, invocation, false);
					invocation.Arguments[index] = byRefValue;
				}

				index++;
			}
		}

		public object ReadArgumentFromStream(BinaryReader r, IInvocation invocation, bool canAttemptToInstantiate)
		{
			RemotingReferenceType referenceType = (RemotingReferenceType)r.ReadInt32();
			if (referenceType == RemotingReferenceType.NullPointer)
			{
				return null;
			}
			if (referenceType == RemotingReferenceType.SerializedItem)
			{
				int argumentLen = r.ReadInt32();
				byte[] argumentData = r.ReadBytes(argumentLen);
				MemoryStream ms = new MemoryStream(argumentData, false);
#pragma warning disable 618
				object decodedArg = _formatter.Deserialize(ms);
#pragma warning restore 618
				return decodedArg;
			}
			else if (referenceType == RemotingReferenceType.RemoteReference)
			{
				// The server sends a reference to an object that he owns
				// This code currently returns a new proxy, even if the server repeatedly returns the same instance
				string typeName = r.ReadString();
				string objectId = r.ReadString();
				var type = Type.GetType(typeName);
				if (type == null)
				{
					throw new RemotingException("Unknown type found in argument stream", RemotingExceptionKind.ProxyManagementError);
				}

				if (_instanceManager.TryGetObjectFromId(objectId, out object instance))
				{
					return instance;
				}

				if (!_instanceManager.IsRemoteInstanceId(objectId))
				{
					throw new InvalidOperationException("Got an instance that should be local but it isn't");
				}

				// Create a class proxy with all interfaces proxied as well.
				var interfaces = type.GetInterfaces();
				var invocationMethodReturnType = invocation.Method?.ReturnType; // This is null in case of a constructor
				if (invocationMethodReturnType != null && invocationMethodReturnType.IsInterface)
				{
					// If the call returns an interface, only create an interface proxy, because we might not be able to instantiate the actual class (because it's not public, it's sealed, has no public ctors, etc)
					instance = _proxyGenerator.CreateInterfaceProxyWithoutTarget(invocationMethodReturnType, interfaces, Interceptor);
				}
				else
				{
					if (canAttemptToInstantiate)
					{
						instance = _proxyGenerator.CreateClassProxy(type, interfaces, ProxyGenerationOptions.Default, invocation.Arguments, Interceptor);
					}
					else
					{
						instance = _proxyGenerator.CreateClassProxy(type, interfaces, Interceptor);
					}
				}

				_instanceManager.AddInstance(instance, objectId);
				return instance;
			}
			else if (referenceType == RemotingReferenceType.InstanceOfSystemType)
			{
				string typeName = r.ReadString();
				Type t = Server.GetTypeFromAnyAssembly(typeName);
				return t;
			}

			throw new RemotingException("Unknown argument type", RemotingExceptionKind.UnsupportedOperation);
		}
	}
}
