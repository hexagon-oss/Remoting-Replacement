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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NewRemoting
{
	/// <summary>
	/// An instance of this class represents the client side of a remoting interface.
	/// This connects to a host object that can run on a different computer (or in a separate process on the same computer)
	/// </summary>
	public sealed class Client : IDisposable
	{
		public const int DefaultNetworkPort = 23456;

		private readonly ClientSideInterceptor _interceptor;
		private readonly MessageHandler _messageHandler;
		private readonly InstanceManager _instanceManager;
		private readonly FormatterFactory _formatterFactory;

		private TcpClient _client;
		private BinaryWriter _writer;
		private BinaryReader _reader;
		private DefaultProxyBuilder _builder;
		private ProxyGenerator _proxy;
		private IFormatter _formatter;
		private Server _server;
		private object _accessLock;
		private TcpClient _serverLink;
		private bool _connectionConfigured;

		/// <summary>
		/// Creates a remoting client for the given server and opens the network connection
		/// </summary>
		/// <param name="server">Server name or IP</param>
		/// <param name="port">Network port</param>
		/// <param name="logger">Optional logging sink (for diagnostic messages)</param>
		public Client(string server, int port, ILogger logger = null)
		{
			Logger = logger ?? NullLogger.Instance;
			_accessLock = new object();
			_connectionConfigured = false;

			_client = new TcpClient(server, port);
			_serverLink = new TcpClient(server, port);
			_writer = new BinaryWriter(_client.GetStream(), Encoding.Unicode);
			_reader = new BinaryReader(_client.GetStream(), Encoding.Unicode);
			_builder = new DefaultProxyBuilder();
			_proxy = new ProxyGenerator(_builder);
			_instanceManager = new InstanceManager(_proxy, Logger);
			_formatterFactory = new FormatterFactory(_instanceManager);
			_formatter = _formatterFactory.CreateFormatter();

			_messageHandler = new MessageHandler(_instanceManager, _formatter);

			_interceptor = new ClientSideInterceptor("Client", _client.GetStream(), _messageHandler, Logger);
			_instanceManager.Interceptor = _interceptor;

			_messageHandler.Init(_interceptor);

			byte[] authenticationData = new byte[100];
			_writer.Write(authenticationData);
			authenticationData[0] = 1;
			_serverLink.GetStream().Write(authenticationData, 0, 100);

			// This is used as return channel
			_server = new Server(_serverLink.GetStream(), _messageHandler, _interceptor);
		}

		/// <summary>
		/// The logger
		/// </summary>
		public ILogger Logger { get; }

		/// <summary>
		/// Gets the list of IP addresses of the current computer
		/// </summary>
		/// <returns></returns>
		public static IPAddress[] LocalIpAddresses()
		{
			var host = Dns.GetHostEntry(Dns.GetHostName());
			return host.AddressList;
		}

		/// <summary>
		/// Returns true if the given object is a proxy.
		/// </summary>
		/// <param name="proxy">The object to test</param>
		/// <returns>True if the object is a proxy, false otherwise</returns>
		public static bool IsRemoteProxy(object proxy)
		{
			return ProxyUtil.IsProxy(proxy);
		}

		/// <summary>
		/// Checks whether the given <see cref="Type"/> is a proxy type.
		/// </summary>
		/// <param name="type">A type instance</param>
		/// <returns>True if the given type is a proxy type</returns>
		public static bool IsProxyType(Type type)
		{
			return ProxyUtil.IsProxyType(type);
		}

		/// <summary>
		/// Checks whether the given object can be used for remoting calls.
		/// </summary>
		/// <param name="obj">The object to query</param>
		/// <returns>True if the given object is derived from <see cref="MarshalByRefObject"/> or if it is already a proxy</returns>
		public static bool IsRemotingCapable(object obj)
		{
			return IsRemoteProxy(obj) || obj is MarshalByRefObject;
		}

		/// <summary>
		/// Completes the infrastructure for RPC calls, namely opens the reverse channel.
		/// </summary>
		internal void Start()
		{
			lock (_accessLock)
			{
				if (!_connectionConfigured)
				{
					_server.StartProcessing();
					RemotingCallHeader openReturnChannel =
						new RemotingCallHeader(RemotingFunctionType.OpenReverseChannel, 0);
					openReturnChannel.WriteTo(_writer);
					var addresses = LocalIpAddresses();
					var addressToUse = addresses.First(x => x.AddressFamily == AddressFamily.InterNetwork);
					_writer.Write(addressToUse.ToString());
					_writer.Write(_server.NetworkPort);
					_connectionConfigured = true;
				}
			}
		}

		/// <summary>
		/// Terminates the remote server process.
		/// This should only be used after the client has no proxy instances in use any more.
		/// </summary>
		public void ShutdownServer()
		{
			lock (_accessLock)
			{
				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.ShutdownServer, 0);
				try
				{
					hd.WriteTo(_writer);
				}
				catch (System.IO.IOException x)
				{
					Logger.LogError(x, "Sending termination command failed. Server already down?");
				}
			}
		}

		/// <summary>
		/// Creates an instance of the given type on the remote system and returns a reference to it.
		/// The type must derive from <see cref="MarshalByRefObject"/> and have a public constructor that takes the given arguments. The server process
		/// must be able to locate the assembly that contains the type. For best results, the type should NOT be sealed, or you might
		/// not get a proxy back that mimics the full interface of the given instance, but only an interface implemented by the
		/// target type.
		/// </summary>
		/// <typeparam name="T">The type of the instance to generate</typeparam>
		/// <param name="args">The list of constructor arguments for the type</param>
		/// <returns>A proxy representing the remote instance.</returns>
		/// <exception cref="RemotingException">The type is not derived from <see cref="MarshalByRefObject"/> or there's no suitable constructor</exception>
		/// <exception cref="InvalidCastException">The type is probably sealed</exception>
		public T CreateRemoteInstance<T>(params object[] args)
			where T : MarshalByRefObject
		{
			return (T)CreateRemoteInstance(typeof(T), args);
		}

		/// <summary>
		/// Creates an instance of the given type on the remote system and returns a reference to it.
		/// The type must derive from <see cref="MarshalByRefObject"/> and have a public constructor that takes the given arguments. The server process
		/// must be able to locate the assembly that contains the type. For best results, the type should NOT be sealed, or you might
		/// not get a proxy back that mimics the full interface of the given instance, but only an interface implemented by the
		/// target type.
		/// </summary>
		/// <param name="typeOfInstance">The type of the instance to generate</param>
		/// <param name="args">The list of constructor arguments for the type</param>
		/// <returns>A proxy representing the remote instance. This attempts to return a proxy for the class type,
		/// but might return a proxy that represents an interface only, particularly if <paramref name="typeOfInstance"/> is sealed.</returns>
		/// <exception cref="RemotingException">The type is not derived from <see cref="MarshalByRefObject"/> or there's no suitable constructor</exception>
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

			ManualInvocation dummyInvocation = new ManualInvocation(ctorType, args);
			using ClientSideInterceptor.CallContext ctx = _interceptor.CreateCallContext(dummyInvocation, sequence);

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
						(int)0); // Currently, we do not need the correct ctor identifier, since there can only be one default ctor
					_writer.Write((int)0); // and no generic args, anyway
				}
				else
				{
					RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.CreateInstance, sequence);
					hd.WriteTo(_writer);
					_writer.Write(typeOfInstance.AssemblyQualifiedName);
					_writer.Write(string.Empty);
					_writer.Write(
						(int)0); // we let the server resolve the correct ctor to use, based on the argument types
					_writer.Write((int)0); // and no generic args, anyway
					_writer.Write(args.Length); // but we need to provide the number of arguments that follow
					foreach (var a in args)
					{
						_messageHandler.WriteArgumentToStream(_writer, a);
					}
				}
			}

			// Just the reply is interesting for us
			_interceptor.WaitForReply(dummyInvocation, ctx);

			return dummyInvocation.ReturnValue;

		}

		/// <summary>
		/// Requests an instance of type T from the remote server. This is typically used with interface types only.
		/// <seealso cref="RequestRemoteInstance"/>
		/// </summary>
		/// <typeparam name="T">The type to get</typeparam>
		/// <returns>An instance of T, if available</returns>
		public T RequestRemoteInstance<T>()
		{
			return (T)RequestRemoteInstance(typeof(T));
		}

		/// <summary>
		/// Requests an instance of the given type from the remote server. This is typically used with interface types only.
		/// This is used to query references that have been statically registered on the server, using the <see cref="ServiceContainer"/>.
		/// </summary>
		/// <param name="typeOfInstance">The type to get</param>
		/// <returns>A proxy for the given remote instance, if available</returns>
		public object RequestRemoteInstance(Type typeOfInstance)
		{
			Start();

			int sequence = _interceptor.NextSequenceNumber();
			// The method is irrelevant here, so take just any method of the given type
			ManualInvocation dummyInvocation = new ManualInvocation(typeOfInstance);
			using ClientSideInterceptor.CallContext ctx = _interceptor.CreateCallContext(dummyInvocation, sequence);

			lock (_accessLock)
			{
				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.RequestServiceReference, sequence);
				hd.WriteTo(_writer);
				_writer.Write(typeOfInstance.AssemblyQualifiedName);
				_writer.Write(string.Empty);
				_writer.Write((int)0); // No ctor is being called
				_writer.Write((int)0); // and no generic args, anyway

			}

			_interceptor.WaitForReply(dummyInvocation, ctx);

			return dummyInvocation.ReturnValue;
		}

		public void Dispose()
		{
			lock (_accessLock)
			{
				_server.Terminate();
				_server.Dispose();
				_client.Dispose();
				_interceptor.Dispose();
				_instanceManager.Clear();
			}
		}
	}
}
