using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;

// BinaryFormatter shouldn't be used
#pragma warning disable SYSLIB0011
namespace NewRemoting
{
	internal sealed class ClientSideInterceptor : IInterceptor, IProxyGenerationHook, IDisposable
	{
		private readonly string _side;
		private readonly TcpClient _serverLink;
		private readonly IInternalClient _remotingClient;
		private readonly ProxyGenerator _proxyGenerator;
		private IFormatter m_formatter;
		private int _sequence;
		private ConcurrentDictionary<int, (IInvocation, AutoResetEvent eventToTrigger)> _pendingInvocations;
		private Thread _receiverThread;
		private bool _receiving;

		public ClientSideInterceptor(String side, TcpClient serverLink, IInternalClient remotingClient, ProxyGenerator proxyGenerator, IFormatter formatter)
		{
			DebuggerToStringBehavior = DebuggerToStringBehavior.ReturnProxyName;
			_sequence = side == "Client" ? 1 : 10000;
			_side = side;
			_serverLink = serverLink;
			_remotingClient = remotingClient;
			_proxyGenerator = proxyGenerator;
			m_formatter = formatter;
			_pendingInvocations = new();
			_receiverThread = new Thread(ReceiverThread);
			_receiverThread.Name = "ClientSideInterceptor - Receiver - " + side;
			_receiving = true;
			_receiverThread.Start();
		}

		public DebuggerToStringBehavior DebuggerToStringBehavior
		{
			get;
			set;
		}

		internal int NextSequenceNumber()
		{
			return Interlocked.Increment(ref _sequence);
		}

		public void Intercept(IInvocation invocation)
		{
			string methodName = invocation.Method.ToString();

			// Todo: Check this stuff
			if (methodName == "ToString()" && DebuggerToStringBehavior != DebuggerToStringBehavior.EvaluateRemotely)
			{
				invocation.ReturnValue = "Remote proxy";
				return;
			}

			int thisSeq;

			MemoryStream rawDataMessage = new MemoryStream(1024);
			BinaryWriter writer = new BinaryWriter(rawDataMessage, Encoding.Unicode);

			lock (_remotingClient.CommunicationLinkLock)
			{
				thisSeq = NextSequenceNumber();

				Debug.WriteLine($"{_side}: Intercepting {invocation.Method}, sequence {thisSeq}");

				// Console.WriteLine($"Here should be a call to {invocation.Method}");
				MethodInfo me = invocation.Method;
				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.MethodCall, thisSeq);

				if (me.IsStatic)
				{
					throw new RemotingException("Remote-calling a static method? No.", RemotingExceptionKind.UnsupportedOperation);
				}

				if (!_remotingClient.TryGetRemoteInstance(invocation.Proxy, out var remoteInstanceId))
				{
					throw new RemotingException("Not a valid remoting proxy", RemotingExceptionKind.ProxyManagementError);
				}

				hd.WriteTo(writer);
				writer.Write(remoteInstanceId);
				// Also transmit the type of the calling object (if the method is called on an interface, this is different from the actual object)
				if (me.DeclaringType != null)
				{
					writer.Write(me.DeclaringType.AssemblyQualifiedName);
				}
				else
				{
					writer.Write(string.Empty);
				}

				writer.Write(me.MetadataToken);
				if (me.ContainsGenericParameters)
				{
					// This should never happen (or the compiler has done something wrong)
					throw new RemotingException("Cannot call methods with open generic arguments", RemotingExceptionKind.UnsupportedOperation);
				}

				var genericArgs = me.GetGenericArguments();
				writer.Write((int)genericArgs.Length);
				foreach (var genericType in genericArgs)
				{
					string arg = genericType.AssemblyQualifiedName;
					if (arg == null)
					{
						throw new RemotingException("Unresolved generic type or some other undefined case", RemotingExceptionKind.UnsupportedOperation);
					}

					writer.Write(arg);
				}

				writer.Write(invocation.Arguments.Length);

				foreach (var argument in invocation.Arguments)
				{
					WriteArgumentToStream(writer, argument);
				}

				// now finally write the stream to the network. That way, we don't send incomplete messages if an exception happens encoding a parameter.
				rawDataMessage.Position = 0;
				rawDataMessage.CopyTo(_serverLink.GetStream());
			}

			WaitForReply(invocation, thisSeq);
		}

		internal void WaitForReply(IInvocation invocation, int thisSeq)
		{
			AutoResetEvent ev = new AutoResetEvent(false);
			if (!_pendingInvocations.TryAdd(thisSeq, (invocation, ev)))
			{
				// This really shouldn't happen
				throw new InvalidOperationException("A call with the same id is already being processed");
			}

			// The event is signaled by the receiver thread when the message was processed
			ev.WaitOne();
			ev.Dispose();
			Debug.WriteLine($"{_side}: {invocation.Method} done.");
		}

		private void ReceiverThread()
		{
			var reader = new BinaryReader(_serverLink.GetStream(), Encoding.Unicode);
			try
			{
				while (_receiving)
				{
					RemotingCallHeader hdReturnValue = default;
					// This read is blocking
					if (!hdReturnValue.ReadFrom(reader))
					{
						throw new RemotingException("Unexpected reply or stream out of sync", RemotingExceptionKind.ProtocolError);
					}

					Debug.WriteLine($"{_side}: Decoding message {hdReturnValue.Sequence} of type {hdReturnValue.Function}");

					if (hdReturnValue.Function != RemotingFunctionType.MethodReply)
					{
						throw new RemotingException("I think only replies should end here", RemotingExceptionKind.ProtocolError);
					}

					if (_pendingInvocations.TryGetValue(hdReturnValue.Sequence, out var item))
					{
						Debug.WriteLine($"{_side}: Receiving reply for {item.Item1.Method}");
						ProcessCallResponse(item.Item1, reader);
						_pendingInvocations.TryRemove(hdReturnValue.Sequence, out _);
						item.eventToTrigger.Set();
					}
					else
					{
						throw new RemotingException($"There's no pending call for sequence id {hdReturnValue.Sequence}", RemotingExceptionKind.ProtocolError);
					}
				}
			}
			catch (IOException x)
			{
				Console.WriteLine("Terminating client receiver thread - Communication Exception: " + x.Message);
				_receiving = false;
			}
		}

		private void ProcessCallResponse(IInvocation invocation, BinaryReader reader)
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
				object returnValue = ReadArgumentFromStream(m_formatter, reader, invocation, true);
				invocation.ReturnValue = returnValue;
				// out or ref arguments on ctors are rare, but not generally forbidden, so we continue here
			}
			else
			{
				MethodInfo me = invocation.Method;
				methodBase = me;
				if (me.ReturnType != typeof(void))
				{
					object returnValue = ReadArgumentFromStream(m_formatter, reader, invocation, true);
					invocation.ReturnValue = returnValue;
				}
			}

			int index = 0;
			foreach (var byRefArguments in methodBase.GetParameters())
			{
				if (byRefArguments.ParameterType.IsByRef)
				{
					object byRefValue = ReadArgumentFromStream(m_formatter, reader, invocation, false);
					invocation.Arguments[index] = byRefValue;
				}

				index++;
			}
		}

		private object ReadArgumentFromStream(IFormatter formatter, BinaryReader r, IInvocation invocation, bool canAttemptToInstantiate)
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
				object decodedArg = formatter.Deserialize(ms);
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

				if (_remotingClient.TryGetLocalInstanceFromReference(objectId, out object instance))
				{
					return instance;
				}

				// Create a class proxy with all interfaces proxied as well.
				var interfaces = type.GetInterfaces();
				var invocationMethodReturnType = invocation.Method?.ReturnType; // This is null in case of a constructor
				if (invocationMethodReturnType != null && invocationMethodReturnType.IsInterface)
				{
					// If the call returns an interface, only create an interface proxy, because we might not be able to instantiate the actual class (because it's not public, it's sealed, has no public ctors, etc)
					instance = _proxyGenerator.CreateInterfaceProxyWithoutTarget(invocationMethodReturnType, interfaces, this);
				}
				else
				{
					if (canAttemptToInstantiate)
					{
						instance = _proxyGenerator.CreateClassProxy(type, interfaces, ProxyGenerationOptions.Default, invocation.Arguments, this);
					}
					else
					{
						instance = _proxyGenerator.CreateClassProxy(type, interfaces, this);
					}
				}

				_remotingClient.AddKnownRemoteInstance(instance, objectId);
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
					string instanceId = _remotingClient.GetIdForLocalObject(del.Target, out bool isNew);
					w.Write(instanceId);
				}
				else
				{
					// The delegate target is a static method
					w.Write(string.Empty);
				}

				string targetId = _remotingClient.GetIdForLocalObject(del, out _);
				w.Write(targetId);
				w.Write(del.Method.DeclaringType.AssemblyQualifiedName);
				w.Write(del.Method.MetadataToken);
			}
			else if (t.IsSerializable)
			{
#pragma warning disable 618
				m_formatter.Serialize(ms, data);
#pragma warning restore 618
				w.Write((int)RemotingReferenceType.SerializedItem);
				w.Write((int)ms.Length);
				w.Write(ms.ToArray(), 0, (int)ms.Length);
			}
			else if (t.IsAssignableTo(typeof(MarshalByRefObject)))
			{
				string objectId = _remotingClient.GetIdForLocalObject(data, out bool isNew);
				w.Write((int)RemotingReferenceType.RemoteReference);
				w.Write(objectId);
				w.Write(data.GetType().AssemblyQualifiedName);
			}
			else
			{
				throw new SerializationException($"Object {data} is neither serializable nor MarshalByRefObject");
			}
		}

		public void MethodsInspected()
		{
		}

		public void NonProxyableMemberNotification(Type type, MemberInfo memberInfo)
		{
			Console.WriteLine($"Type {type} has non-virtual method {memberInfo} - cannot be used for proxying");
		}

		public bool ShouldInterceptMethod(Type type, MethodInfo methodInfo)
		{
			return true;
		}

		public void Dispose()
		{
			_receiving = false;
			_receiverThread?.Join();
			_receiverThread = null;
		}
	}
}
