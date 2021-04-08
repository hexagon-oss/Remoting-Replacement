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
using RemotingServer;

namespace RemotingClient
{
    public class ClientSideInterceptor : IInterceptor, IProxyGenerationHook
    {
        private readonly TcpClient _serverLink;
        private readonly RemotingClient _remotingClient;
        private readonly ProxyGenerator _proxyGenerator;
        private BinaryFormatter m_formatter;
        private BinaryWriter _writer;
        private BinaryReader _reader;
        private int _sequence;

        public ClientSideInterceptor(TcpClient serverLink, RemotingClient remotingClient, ProxyGenerator proxyGenerator)
        {
            _sequence = 1;
            _serverLink = serverLink;
            _remotingClient = remotingClient;
            _proxyGenerator = proxyGenerator;
            m_formatter = new BinaryFormatter();
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
                WriteArgumentToStream(m_formatter, _writer, argument);
            }

            RemotingCallHeader hdReturnValue = default;
            
            if (!hdReturnValue.ReadFrom(_reader))
            {
                throw new InvalidOperationException("Unexpected reply or stream out of sync");
            }

            if (me.ReturnType == typeof(void))
            {
                return;
            }

            object returnValue = ReadArgumentFromStream(m_formatter, _reader);
            invocation.ReturnValue = returnValue;

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
            bool isSerializableArgument = r.ReadBoolean();
            if (isSerializableArgument)
            {
                int argumentLen = r.ReadInt32();
                byte[] argumentData = r.ReadBytes(argumentLen);
                MemoryStream ms = new MemoryStream(argumentData, false);
                object decodedArg = formatter.Deserialize(ms);
                return decodedArg;
            }
            else
            {
                string typeName = r.ReadString();
                string objectId = r.ReadString();
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    throw new InvalidOperationException("Unknown type found in argument stream");
                }

                object instance = _proxyGenerator.CreateClassProxy(type, this);
                _remotingClient.KnownRemoteInstances.Add(instance, objectId);

                return instance;
            }
        }

        public void WriteArgumentToStream(IFormatter formatter, BinaryWriter w, object data)
        {
            MemoryStream ms = new MemoryStream();
            Type t = data.GetType();
            if (t.IsSerializable)
            {
                formatter.Serialize(ms, data);
                w.Write(true);
                w.Write((int)ms.Length);
                w.Write(ms.ToArray(), 0, (int)ms.Length);
            }
            else if (t.IsAssignableTo(typeof(MarshalByRefObject)))
            {
                string objectId = RemotingServer.RemotingServer.GetObjectInstanceId(data);
                w.Write(false);
                w.Write(objectId);
                w.Write(data.GetType().FullName);
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
