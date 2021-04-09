using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
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
        private ConditionalWeakTable<object, string> _knownRemoteInstances;

        /// <summary>
        /// This contains references the client forwards to the server, that is, where the client hosts the actual object and the server gets the proxy.
        /// </summary>
        private Dictionary<string, object> _hardReverseReferences;

        public RemotingClient(string server, int port)
        {
            _knownRemoteInstances = new();
            _hardReverseReferences = new ();
            _client = new TcpClient("localhost", 23456);
            _writer = new BinaryWriter(_client.GetStream(), Encoding.Unicode);
            _reader = new BinaryReader(_client.GetStream(), Encoding.Unicode);
            _builder = new DefaultProxyBuilder();
            _proxy = new ProxyGenerator(_builder);
        }

        internal ConditionalWeakTable<object, string> KnownRemoteInstances => _knownRemoteInstances;

        internal IDictionary<string, object> ClientReferences => _hardReverseReferences;

        public T CreateRemoteInstance<T>(Type typeOfInstance) where T : MarshalByRefObject
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
            RemotingReferenceType remoteType = (RemotingReferenceType)_reader.ReadInt32();

            if (hdParseSuccess == false || remoteType != RemotingReferenceType.NewProxy)
            {
                throw new InvalidDataException("Unexpected reply");
            }

            string typeName = _reader.ReadString();
            string objectId = _reader.ReadString();

            var interceptor = new ClientSideInterceptor(_client, this, _proxy);

            ProxyGenerationOptions options = new ProxyGenerationOptions(interceptor);

            object instance = _proxy.CreateClassProxy(typeOfInstance, options, interceptor);
            _knownRemoteInstances.Add(instance, objectId);
            return (T)instance;
        }
    }
}
