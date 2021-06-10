using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Castle.DynamicProxy.Generators.Emitters.SimpleAST;

namespace NewRemoting
{
	public sealed class Client : IDisposable
	{
		public const int DefaultNetworkPort = 23456;

		private TcpClient _client;
		private BinaryWriter _writer;
		private BinaryReader _reader;
		private DefaultProxyBuilder _builder;
		private ProxyGenerator _proxy;
		private IFormatter _formatter;
		private Server _server;
		private object _accessLock;
		
		private readonly ClientSideInterceptor _interceptor;
		private readonly MessageHandler _messageHandler;
		private readonly InstanceManager _instanceManager;

		public Client(string server, int port)
		{
			_accessLock = new object();
			_formatter = new BinaryFormatter();
			_client = new TcpClient(server, port);
			_writer = new BinaryWriter(_client.GetStream(), Encoding.Unicode);
			_reader = new BinaryReader(_client.GetStream(), Encoding.Unicode);
			_builder = new DefaultProxyBuilder();
			_proxy = new ProxyGenerator(_builder);
			_instanceManager = new InstanceManager();

			_messageHandler = new MessageHandler(_instanceManager, _proxy, _formatter);

			_interceptor = new ClientSideInterceptor("Client", _client, _messageHandler);

			_messageHandler.Init(_interceptor);

			// This is used as return channel
			_server = new Server(port + 1, _messageHandler, _interceptor);
		}

		public IPAddress[] LocalIpAddresses()
		{
			var host = Dns.GetHostEntry(Dns.GetHostName());
			return host.AddressList;
		}

		public static bool IsRemoteProxy(object proxy)
		{
			return ProxyUtil.IsProxy(proxy);
		}

		public void Start()
		{
			lock (_accessLock)
			{
				if (!_server.IsRunning)
				{
					_server.StartListening();
					RemotingCallHeader openReturnChannel =
						new RemotingCallHeader(RemotingFunctionType.OpenReverseChannel, 0);
					openReturnChannel.WriteTo(_writer);
					var addresses = LocalIpAddresses();
					var addressToUse = addresses.First(x => x.AddressFamily == AddressFamily.InterNetwork);
					_writer.Write(addressToUse.ToString());
					_writer.Write(_server.NetworkPort);
				}
			}
		}

		public void ShutdownServer()
		{
			lock (_accessLock)
			{
				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.ShutdownServer, 0);
				hd.WriteTo(_writer);
			}
		}

		public T CreateRemoteInstance<T>(params object[] args) where T : MarshalByRefObject
		{
			return (T) CreateRemoteInstance(typeof(T), args);
		}

		public object CreateRemoteInstance(Type typeOfInstance, params object[] args)
		{
			if (typeOfInstance == null)
			{
				throw new ArgumentNullException(nameof(typeOfInstance));
			}

			if (!typeOfInstance.IsAssignableTo(typeof(MarshalByRefObject)))
			{
				throw new RemotingException("Can only create instances of type MarshalByRefObject remotely",
					RemotingExceptionKind.UnsupportedOperation);
			}

			Start();

			int sequence = _interceptor.NextSequenceNumber();

			Type[] argumentTypes = args.Select(x => x.GetType()).ToArray();

			// The type of the constructor that will be called
			ConstructorInfo ctorType = typeOfInstance.GetConstructor(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, argumentTypes, null);

			if (ctorType == null)
			{
				throw new RemotingException($"No public default constructor found on type {typeOfInstance}.", RemotingExceptionKind.UnsupportedOperation);
			}

			lock (_accessLock)
			{
				if (args == null || args.Length == 0)
				{
					RemotingCallHeader hd =
						new RemotingCallHeader(RemotingFunctionType.CreateInstanceWithDefaultCtor, sequence);
					hd.WriteTo(_writer);
					_writer.Write(typeOfInstance.AssemblyQualifiedName);
					_writer.Write(string.Empty);
					_writer.Write(
						(int) 0); // Currently, we do not need the correct ctor identifier, since there can only be one default ctor
					_writer.Write((int) 0); // and no generic args, anyway
				}
				else
				{
					RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.CreateInstance, sequence);
					hd.WriteTo(_writer);
					_writer.Write(typeOfInstance.AssemblyQualifiedName);
					_writer.Write(string.Empty);
					_writer.Write(
						(int) 0); // we let the server resolve the correct ctor to use, based on the argument types
					_writer.Write((int) 0); // and no generic args, anyway
					_writer.Write(args.Length); // but we need to provide the number of arguments that follow
					foreach (var a in args)
					{
						_messageHandler.WriteArgumentToStream(_writer, a);
					}
				}
			}

			// Just the reply is interesting for us
			ManualInvocation dummyInvocation = new ManualInvocation(ctorType, args);
			_interceptor.WaitForReply(dummyInvocation, sequence);

			return dummyInvocation.ReturnValue;
			/*
			RemotingCallHeader hdReply = default;
			bool hdParseSuccess = hdReply.ReadFrom(_reader);
			RemotingReferenceType remoteType = (RemotingReferenceType) _reader.ReadInt32();

			if (hdParseSuccess == false || remoteType != RemotingReferenceType.RemoteReference)
			{
			    throw new RemotingException("Unexpected reply", RemotingExceptionKind.ProtocolError);
			}

			string typeName = _reader.ReadString();
			string objectId = _reader.ReadString();
			
			ProxyGenerationOptions options = new ProxyGenerationOptions(_interceptor);

			object instance = _proxy.CreateClassProxy(typeOfInstance, typeOfInstance.GetInterfaces(), options, args, _interceptor);
			_knownRemoteInstances.Add(instance, objectId);
			return instance;*/

		}

		public T RequestRemoteInstance<T>()
		{
			return (T) RequestRemoteInstance(typeof(T));
		}

		public object RequestRemoteInstance(Type typeOfInstance)
		{
			Start();

			lock (_accessLock)
			{

				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.RequestServiceReference, 0);
				hd.WriteTo(_writer);
				_writer.Write(typeOfInstance.AssemblyQualifiedName);
				_writer.Write(string.Empty);
				_writer.Write(
					(int) 0); // Currently, we do not need the correct ctor identifier, since there can only be one default ctor
				_writer.Write((int) 0); // and no generic args, anyway
				RemotingCallHeader hdReply = default;
				bool hdParseSuccess = hdReply.ReadFrom(_reader);
				RemotingReferenceType remoteType = (RemotingReferenceType) _reader.ReadInt32();

				if (hdParseSuccess == false)
				{
					throw new RemotingException("Unexpected reply", RemotingExceptionKind.ProtocolError);
				}

				string typeName = _reader.ReadString();
				string objectId = _reader.ReadString();

				ProxyGenerationOptions options = new ProxyGenerationOptions(_interceptor);
				var actualType = Type.GetType(typeName);

				object instance =
					_proxy.CreateClassProxy(typeOfInstance, actualType.GetInterfaces(), options, _interceptor);
				_instanceManager.AddInstance(instance, objectId);
				return instance;
			}
		}

		public void Dispose()
		{
			lock (_accessLock)
			{
				_server.Terminate();
				_server.Dispose();
				_client.Dispose();
			}
		}
	}
}
