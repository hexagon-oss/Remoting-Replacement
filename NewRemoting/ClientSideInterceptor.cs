using System;
using System.Collections.Generic;
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
    public class ClientSideInterceptor : IInterceptor, IProxyGenerationHook
    {
        private readonly TcpClient _serverLink;
        private readonly RemotingClient _remotingClient;
        private readonly ProxyGenerator _proxyGenerator;
        private IFormatter m_formatter;
        private BinaryWriter _writer;
        private BinaryReader _reader;
        private int _sequence;

        public ClientSideInterceptor(TcpClient serverLink, RemotingClient remotingClient, ProxyGenerator proxyGenerator, IFormatter formatter)
        {
            _sequence = 1;
            _serverLink = serverLink;
            _remotingClient = remotingClient;
            _proxyGenerator = proxyGenerator;
            m_formatter = formatter;
            _writer = new BinaryWriter(_serverLink.GetStream(), Encoding.Unicode);
            _reader = new BinaryReader(_serverLink.GetStream(), Encoding.Unicode);
        }

        public void Intercept(IInvocation invocation)
        {
            int thisSeq = _sequence++;
            // Console.WriteLine($"Here should be a call to {invocation.Method}");
            MethodInfo me = invocation.Method;
            RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.MethodCall, thisSeq);

            if (me.IsStatic)
            {
                throw new InvalidOperationException("Remote-calling a static method? No.");
            }

            if (!_remotingClient.KnownRemoteInstances.TryGetValue(invocation.Proxy, out var remoteInstanceId))
            {
                throw new InvalidOperationException("Not a valid remoting proxy");
            }

            hd.WriteTo(_writer);
            _writer.Write(remoteInstanceId);
            _writer.Write(me.Name);
            _writer.Write(invocation.Arguments.Length);

            foreach (var argument in invocation.Arguments)
            {
                WriteArgumentToStream(_writer, argument);
            }

            RemotingCallHeader hdReturnValue = default;
            
            if (!hdReturnValue.ReadFrom(_reader))
            {
                throw new InvalidOperationException("Unexpected reply or stream out of sync");
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

            /* MemoryStream ms = new MemoryStream();
            m_formatter.Serialize(ms, invocation.Method.MetadataToken);

            ms.Position = 0;
            IInvocation deserialized = (IInvocation)m_formatter.Deserialize(ms);
            
            deserialized.Proceed();

            MemoryStream reply = new MemoryStream();
            m_formatter.Serialize(reply, deserialized);
            reply.Position = 0;

            IInvocation finalResult = (IInvocation) m_formatter.Deserialize(reply);
            invocation.ReturnValue = finalResult.ReturnValue;*/
        }

        public object ReadArgumentFromStream(IFormatter formatter, BinaryReader r)
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
                    throw new InvalidOperationException("Unknown type found in argument stream");
                }

                object instance = _proxyGenerator.CreateClassProxy(type, this);
                _remotingClient.KnownRemoteInstances.AddOrUpdate(instance, objectId);

                return instance;
            }
            else if (referenceType == RemotingReferenceType.RemoteReference)
            {
                // The server returns a reference to an object that the client owns
                string typeName = r.ReadString();
                string objectId = r.ReadString();
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    throw new InvalidOperationException("Unknown type found in argument stream");
                }

                if (!_remotingClient.ClientReferences.TryGetValue(objectId, out var instance))
                {
                    throw new InvalidOperationException("Client-Hosted object does not exist");
                }

                return instance;
            }

            throw new NotSupportedException("Unknown argument type");
        }

        private void WriteArgumentToStream(BinaryWriter w, object data)
        {
            MemoryStream ms = new MemoryStream();
            Type t = data.GetType();
            if (t.IsSerializable)
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
                string objectId = RemotingServer.GetObjectInstanceId(data);
                if (!_remotingClient.ClientReferences.TryGetValue(objectId, out object localRef))
                {
                    _remotingClient.ClientReferences.Add(objectId, localRef);
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
