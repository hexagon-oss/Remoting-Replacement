using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using RemotingServer;

namespace RemotingClient
{
    public class RemotingClient
    {
        private TcpClient _client;
        private BinaryWriter _writer;
        private BinaryReader _reader;
        private DefaultProxyBuilder _builder;
        private ProxyGenerator _proxy;
        private Dictionary<object, string> _knownRemoteInstances;

        public RemotingClient(string server, int port)
        {
            _knownRemoteInstances = new();
            _client = new TcpClient("localhost", 23456);
            _writer = new BinaryWriter(_client.GetStream(), Encoding.Unicode);
            _reader = new BinaryReader(_client.GetStream(), Encoding.Unicode);
            _builder = new DefaultProxyBuilder();
            _proxy = new ProxyGenerator(_builder);
        }

        internal IDictionary<object, string> KnownRemoteInstances => _knownRemoteInstances;

        public object CreateRemoteInstance(Type typeOfInstance)
        {
            if (!typeOfInstance.IsAssignableTo(typeof(MarshalByRefObject)))
            {
                throw new NotSupportedException("Can only create instances of type MarshalByRefObject remotely");
            }

            RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.CreateInstanceWithDefaultCtor, 0);
            hd.WriteTo(_writer);
            _writer.Write(typeOfInstance.FullName);
            _writer.Write(".ctor");
            RemotingCallHeader hdReply = default;
            bool hdParseSuccess = hdReply.ReadFrom(_reader);
            bool isValueType = _reader.ReadBoolean();

            if (hdParseSuccess == false || isValueType)
            {
                throw new InvalidDataException("Unexpected reply");
            }

            string typeName = _reader.ReadString();
            string objectId = _reader.ReadString();

            object instance = _proxy.CreateClassProxy(typeOfInstance, new ClientSideInterceptor(_client, this, _proxy));
            if (instance.GetType().IsAssignableTo(Type.GetType(typeName)))
            {
                throw new InvalidOperationException("Got a different object than requested");
            }
            _knownRemoteInstances.Add(instance, objectId);
            return instance;
        }
    }
}
