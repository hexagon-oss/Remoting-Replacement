using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;

// BinaryFormatter shouldn't be used
#pragma warning disable SYSLIB0011
namespace NewRemoting
{
    internal class ClientSideInterceptor : IInterceptor, IProxyGenerationHook
    {
        private readonly TcpClient _serverLink;
        private readonly IInternalClient _remotingClient;
        private readonly ProxyGenerator _proxyGenerator;
        private IFormatter m_formatter;
        private BinaryWriter _writer;
        private BinaryReader _reader;
        private int _sequence;

        public ClientSideInterceptor(TcpClient serverLink, IInternalClient remotingClient, ProxyGenerator proxyGenerator, IFormatter formatter)
        {
            DebuggerToStringBehavior = DebuggerToStringBehavior.ReturnProxyName;
            _sequence = 1;
            _serverLink = serverLink;
            _remotingClient = remotingClient;
            _proxyGenerator = proxyGenerator;
            m_formatter = formatter;
            _writer = new BinaryWriter(_serverLink.GetStream(), Encoding.Unicode);
            _reader = new BinaryReader(_serverLink.GetStream(), Encoding.Unicode);
        }

        public DebuggerToStringBehavior DebuggerToStringBehavior
        {
            get;
            set;
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
            Console.WriteLine($"Intercepting {invocation.Method}");

            lock (_remotingClient.CommunicationLinkLock)
            {
                int thisSeq = _sequence++;
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

                hd.WriteTo(_writer);
                _writer.Write(remoteInstanceId);
                // Also transmit the type of the calling object (if the method is called on an interface, this is different from the actual object)
                if (me.DeclaringType != null)
                {
                    _writer.Write(me.DeclaringType.AssemblyQualifiedName);
                }
                else
                {
                    _writer.Write(string.Empty);
                }

                _writer.Write(me.MetadataToken);
                if (me.ContainsGenericParameters)
                {
                    // This should never happen (or the compiler has done something wrong)
                    throw new RemotingException("Cannot call methods with open generic arguments", RemotingExceptionKind.UnsupportedOperation);
                }

                var genericArgs = me.GetGenericArguments();
                _writer.Write((int) genericArgs.Length);
                foreach (var genericType in genericArgs)
                {
                    string arg = genericType.AssemblyQualifiedName;
                    if (arg == null)
                    {
                        throw new RemotingException("Unresolved generic type or some other undefined case", RemotingExceptionKind.UnsupportedOperation);
                    }

                    _writer.Write(arg);
                }

                _writer.Write(invocation.Arguments.Length);

                foreach (var argument in invocation.Arguments)
                {
                    WriteArgumentToStream(_writer, argument);
                }

                RemotingCallHeader hdReturnValue = default;

                if (!hdReturnValue.ReadFrom(_reader))
                {
                    throw new RemotingException("Unexpected reply or stream out of sync", RemotingExceptionKind.ProtocolError);
                }

                if (me.ReturnType != typeof(void))
                {
                    object returnValue = ReadArgumentFromStream(m_formatter, _reader);
                    invocation.ReturnValue = returnValue;
                }

                int index = 0;
                foreach (var byRefArguments in me.GetParameters())
                {
                    if (byRefArguments.ParameterType.IsByRef)
                    {
                        object byRefValue = ReadArgumentFromStream(m_formatter, _reader);
                        invocation.Arguments[index] = byRefValue;
                    }

                    index++;
                }
            }
        }

        private object ReadArgumentFromStream(IFormatter formatter, BinaryReader r)
        {
            RemotingReferenceType referenceType = (RemotingReferenceType)r.ReadInt32();
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
            else if (referenceType == RemotingReferenceType.NewProxy)
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

                // Create a class proxy with all interfaces proxied as well.
                var interfaces = type.GetInterfaces();
                object instance = _proxyGenerator.CreateClassProxy(type, interfaces, this);
                _remotingClient.AddKnownRemoteInstance(instance, objectId);

                return instance;
            }
            else if (referenceType == RemotingReferenceType.RemoteReference)
            {
                // The server returns a reference to an object that the client owns or already knows
                string typeName = r.ReadString();
                string objectId = r.ReadString();
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    throw new RemotingException("Unknown type found in argument stream", RemotingExceptionKind.ProxyManagementError);
                }
                
                var instance = _remotingClient.GetLocalInstanceFromReference(objectId);
                if (instance == null)
                {
                    // Is it valid to create a new proxy if this happens?
                    throw new RemotingException("Remote instance not found", RemotingExceptionKind.ProxyManagementError);
                }

                return instance;
            }

            throw new RemotingException("Unknown argument type", RemotingExceptionKind.UnsupportedOperation);
        }

        private void WriteArgumentToStream(BinaryWriter w, object data)
        {
            MemoryStream ms = new MemoryStream();
            Type t = data.GetType();
            if (data is Delegate del)
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
                if (isNew)
                {
                    w.Write((int)RemotingReferenceType.NewProxy);
                }
                else
                {
                    w.Write((int)RemotingReferenceType.RemoteReference);
                }
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
    }
}
