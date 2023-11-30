using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Castle.Core.Internal;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NewRemoting.Toolkit;

namespace NewRemoting
{
	/// <summary>
	/// An instance of this class represents the client side of a remoting interface.
	/// This connects to a host object that can run on a different computer (or in a separate process on the same computer)
	/// </summary>
	public sealed class Client : IDisposable
	{
		private const string ProxyNamespace = "Castle.Proxies";
		public const int DefaultNetworkPort = 4600;

		private readonly ClientSideInterceptor _interceptor;
		private readonly MessageHandler _messageHandler;
		private readonly InstanceManager _instanceManager;
		private readonly FormatterFactory _formatterFactory;
		private readonly AuthenticationInformation _clientAuthentication;

		private TcpClient _client;
		private BinaryWriter _writer;
		private BinaryReader _reader;
		private DefaultProxyBuilder _builder;
		private ProxyGenerator _proxy;
		private JsonSerializerOptions _formatter;
		private Server _server;
		private object _accessLock;
		private TcpClient _serverLink;
		private bool _connectionConfigured;
		private bool _disconnected;
		private X509Certificate2 _certificate;

		/// <summary>
		/// Creates a remoting client for the given server and opens the network connection
		/// </summary>
		/// <param name="server">Server name or IP</param>
		/// <param name="port">Network port</param>
		/// <param name="authenticationInformation"> credentials for authentication</param>
		/// <param name="instanceManagerLogger">Optional logging sink (for diagnostic messages)</param>
		/// <param name="connectionLogger">Logger to trace this very client</param>
		public Client(string server, int port, AuthenticationInformation authenticationInformation, ILogger instanceManagerLogger = null, ILogger connectionLogger = null)
		{
			_clientAuthentication = authenticationInformation;
			var instanceLogger = instanceManagerLogger ?? NullLogger.Instance;
			Logger = connectionLogger ?? NullLogger.Instance;
			_accessLock = new object();
			_connectionConfigured = false;
			_disconnected = false;

			var msg = authenticationInformation != null ? authenticationInformation.CertificateFileName : string.Empty;
			Logger.LogInformation($"Creating client {msg}");
			_client = new TcpClient(server, port);

			_serverLink = new TcpClient(server, port);
			Stream stream = _client.GetStream();
			if (authenticationInformation != null && !string.IsNullOrEmpty(authenticationInformation.CertificateFileName))
			{
				Logger.LogInformation("Client authentication started.");
				stream = Authenticate(_client, server);
				Logger.LogInformation("Client authentication done.");
			}

			Logger.LogInformation($"stream created");
			_writer = new BinaryWriter(stream, MessageHandler.DefaultStringEncoding);
			_reader = new BinaryReader(stream, MessageHandler.DefaultStringEncoding);
			_builder = new DefaultProxyBuilder();
			_proxy = new ProxyGenerator(_builder);
			_instanceManager = new InstanceManager(_proxy, instanceLogger);
			_formatterFactory = new FormatterFactory(_instanceManager);

			_messageHandler = new MessageHandler(_instanceManager, _formatterFactory);

			// A client side has only one server, so there's also only one interceptor and only one server side
			_interceptor = new ClientSideInterceptor(string.Empty, _instanceManager.ProcessIdentifier, true, stream, _messageHandler, Logger);
			_instanceManager.AddInterceptor(_interceptor);
			_messageHandler.AddInterceptor(_interceptor);

			Logger.LogInformation(FormattableString.Invariant($"Client to {server} creation - writing basic client authentication header"));
			WriteAuthenticationHeader(stream, false);
			WaitForConnectionReply(stream);
			Logger.LogInformation("Received basic client authentication reply");

			Stream s = _serverLink.GetStream();
			if (authenticationInformation != null && !string.IsNullOrEmpty(authenticationInformation.CertificateFileName))
			{
				Logger.LogInformation("Starting server authentication");
				s = Authenticate(_serverLink, server);
				Logger.LogInformation("Finished server authentication");
			}

			Logger.LogInformation("Writing basic server authentication header");
			WriteAuthenticationHeader(s, true);
			WaitForConnectionReply(s);
			Logger.LogInformation("Received basic server authentication reply");

			_formatter = _formatterFactory.CreateOrGetFormatter(_interceptor.OtherSideProcessId);
			_interceptor.Start();
			Logger.LogInformation("Interceptor started");

			// This is used as return channel
			_server = new Server(s, _messageHandler, _interceptor);
		}

		private void WriteAuthenticationHeader(Stream destination, bool callbackStream)
		{
			// Current use of this connection header:
			// byte        | Function
			// 0           | 0 = forwarding stream, 1 = callback stream
			// 1 - 4       | instance hash of this client
			// 5 - 99      | reserved
			// 100 - 103   | length of identifier
			// 103 -       | our instance identifier in the default encoding
			byte[] authenticationData = new byte[100];
			authenticationData[0] = (byte)(callbackStream ? 1 : 0);

			int instanceHash = _instanceManager.ProcessIdentifier.GetHashCode(StringComparison.Ordinal);
			Array.Copy(BitConverter.GetBytes(instanceHash), 0, authenticationData, 1, 4);
			destination.Write(authenticationData, 0, 100);

			var instanceIdentifier = MessageHandler.DefaultStringEncoding.GetBytes(_instanceManager.ProcessIdentifier);
			var lenBytes = BitConverter.GetBytes(instanceIdentifier.Length);

			destination.Write(lenBytes);
			destination.Write(instanceIdentifier);
		}

		private bool WaitForConnectionReply(Stream destination)
		{
			byte[] authenticationSucceededToken = new byte[4];
			if (destination.Read(authenticationSucceededToken, 0, 4) != 4)
			{
				return false;
			}

			int token = BitConverter.ToInt32(authenticationSucceededToken);
			if (token != Server.AuthenticationSucceededToken)
			{
				return false;
			}

			if (destination.Read(authenticationSucceededToken, 0, 4) != 4)
			{
				return false;
			}

			int length = BitConverter.ToInt32(authenticationSucceededToken);

			byte[] data = new byte[length];
			if (destination.Read(data, 0, length) != length)
			{
				return false;
			}

			_interceptor.OtherSideProcessId = MessageHandler.DefaultStringEncoding.GetString(data);
			return true;
		}

		private SslStream Authenticate(TcpClient client, string targetHost)
		{
			SslStream sslStream = new SslStream(client.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
			// The server name must match the name on the server certificate.
			try
			{
				if (!string.IsNullOrEmpty(_clientAuthentication.CertificateFileName))
				{
					_certificate = new X509Certificate2(_clientAuthentication.CertificateFileName,
						_clientAuthentication.CertificatePassword,
						X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.UserKeySet);
					SslClientAuthenticationOptions options = new SslClientAuthenticationOptions
					{
						TargetHost = targetHost,
						ClientCertificates = new X509CertificateCollection { _certificate },
						CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
					};

					sslStream.AuthenticateAsClient(options);
					Logger?.LogInformation("Client Authentication Done.");
				}

				return sslStream;
			}
			catch (Exception e) when (e is AuthenticationException or CryptographicException)
			{
				Logger?.LogError(e.ToString());
				if (e.InnerException != null)
				{
					Console.WriteLine("Inner exception: {0}", e.InnerException.Message);
				}

				Logger.LogError("Authentication failed - closing the connection.");
				client.Close();
				throw;
			}
		}

		/// <summary>
		/// The logger
		/// </summary>
		public ILogger Logger { get; }

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
		/// Returns the non-proxy type of the instance
		/// </summary>
		/// <param name="instance">A proxy instance</param>
		/// <returns>The type of the underlying real object</returns>
		public static Type GetUnproxiedType(object instance)
		{
			Type t = ProxyUtil.GetUnproxiedType(instance);
			if (IsProxyType(t))
			{
				// Still a proxy? Need to resolve this manually
				return ManualGetUnproxiedType(t);
			}

			return t;
		}

		private static bool ValidateServerCertificate(
			object sender,
			X509Certificate certificate,
			X509Chain chain,
			SslPolicyErrors sslPolicyErrors)
		{
			// todo remove the mismatch case
			if (sslPolicyErrors == SslPolicyErrors.None || sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch ||
				sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors || chain.ChainStatus.Length > 0)
			{
				return true;
			}

			return false;
		}

		internal static Type ManualGetUnproxiedType(Type t)
		{
			if (!IsProxyType(t))
			{
				return t;
			}

			// If t is an interface, this would just return "Object", which is not what we want
			// But we can't test that directly, since a proxy type is never an interface, even if it is constructed from one.
			Type tBase = t.BaseType;
			while (tBase != null && tBase != typeof(object))
			{
				if (!IsProxyType(tBase) && !tBase.FullName.StartsWith(ProxyNamespace, StringComparison.Ordinal))
				{
					return tBase;
				}

				tBase = tBase.BaseType;
			}

			foreach (var interf in t.GetInterfaces())
			{
				if (interf.FullName == null)
				{
					continue;
				}

				if (!interf.FullName.StartsWith(ProxyNamespace, StringComparison.Ordinal) && (t.Name.StartsWith(interf.Name + "Proxy", StringComparison.Ordinal)))
				{
					return interf;
				}
			}

			return t;
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
		/// Returns the instance identifiers of this connection. The first
		/// return value is the local identifier, the second argument the remote identifier.
		/// </summary>
		public (string Local, string Remote) InstanceIdentifiers()
		{
			return (_instanceManager.ProcessIdentifier, _interceptor.OtherSideProcessId);
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
					_server.StartProcessing(_interceptor.OtherSideProcessId);
					RemotingCallHeader openReturnChannel =
						new RemotingCallHeader(RemotingFunctionType.OpenReverseChannel, 0);
					using var lck = openReturnChannel.WriteHeader(_writer);
					var addresses = NetworkUtil.LocalIpAddresses();
					var addressToUse = addresses.First(x => x.AddressFamily == AddressFamily.InterNetwork);
					_writer.Write(addressToUse.ToString());
					_writer.Write(_server.NetworkPort);
					_writer.Write(_instanceManager.ProcessIdentifier);
					_writer.Write(_instanceManager.ProcessIdentifier.GetHashCode(StringComparison.Ordinal));
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
				IDisposable disp = null;
				try
				{
					disp = hd.WriteHeader(_writer);
				}
				catch (System.IO.IOException x)
				{
					Logger.LogError(x, "Sending termination command failed. Server already down?");
				}
				finally
				{
					disp?.Dispose();
				}
			}
		}

		/// <summary>
		/// Verifies the server uses a compatible configuration.
		/// The configuration of the server is incompatible for instance if it doesn't run the same CLR main version.
		/// </summary>
		/// <returns>Null on success, an error message otherwise. It's the callers responsibility to do something about it.</returns>
		public string VerifyMatchingServer()
		{
			var server = RequestRemoteInstance<IRemoteServerService>();
			if (server == null)
			{
				return $"Internal server error: {nameof(IRemoteServerService)} not available.";
			}

			Version localVersion = Environment.Version;
			Version remoteVersion = server.ClrVersion;

			if (localVersion.Major != remoteVersion.Major)
			{
				return
					$"Remote CLR runtime version is {remoteVersion}, local runtime version is {localVersion}. The main version must be equal";
			}

			return null;
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
				throw new RemotingException("Can only create instances of type MarshalByRefObject remotely");
			}

			Start();

			int sequence = _interceptor.NextSequenceNumber();

			Type[] argumentTypes = args.Select(x => x.GetType()).ToArray();

			// The type of the constructor that will be called
			ConstructorInfo ctorType = typeOfInstance.GetConstructor(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, argumentTypes, null);

			if (ctorType == null)
			{
				throw new RemotingException($"No public default constructor found on type {typeOfInstance}.");
			}

			ManualInvocation dummyInvocation = new ManualInvocation(ctorType, args);
			using ClientSideInterceptor.CallContext ctx = _interceptor.CreateCallContext(dummyInvocation, sequence);

			try
			{
				if (args.Length == 0)
				{
					RemotingCallHeader hd =
						new RemotingCallHeader(RemotingFunctionType.CreateInstanceWithDefaultCtor, sequence);
					using (var lck = hd.WriteHeader(_writer))
					{
						_writer.Write(typeOfInstance.AssemblyQualifiedName);
						_writer.Write(string.Empty);
						_writer.Write(string
							.Empty); // Currently, we do not need the correct ctor identifier, since there can only be one default ctor
						_writer.Write((int)0); // and no generic args, anyway
					}
				}
				else
				{
					RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.CreateInstance, sequence);
					using (var lck = hd.WriteHeader(_writer))
					{
						_writer.Write(typeOfInstance.AssemblyQualifiedName);
						_writer.Write(string.Empty);
						_writer.Write(string
							.Empty); // we let the server resolve the correct ctor to use, based on the argument types
						_writer.Write((int)0); // and no generic args, anyway
						_writer.Write(args.Length); // but we need to provide the number of arguments that follow
						foreach (var a in args)
						{
							_messageHandler.WriteArgumentToStream(_writer, a, _interceptor.OtherSideProcessId);
						}
					}
				}
			}
			catch (Exception x) when (x is IOException || x is ObjectDisposedException)
			{
				throw new RemotingException("Error writing to the transport connection. Possibly disconnected from remote side.", x);
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

			try
			{
				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.RequestServiceReference, sequence);
				using (var lck = hd.WriteHeader(_writer))
				{
					_writer.Write(typeOfInstance.AssemblyQualifiedName);
					_writer.Write(string.Empty);
					_writer.Write(string.Empty); // No ctor is being called
					_writer.Write((int)0); // and no generic args, anyway
				}
			}
			catch (Exception x) when (x is IOException || x is ObjectDisposedException)
			{
				throw new RemotingException("Error writing to the transport connection. Possibly disconnected from remote side.", x);
			}

			_interceptor.WaitForReply(dummyInvocation, ctx);

			return dummyInvocation.ReturnValue;
		}

		/// <summary>
		/// Disconnects from the server in a way that does not affect the server state.
		/// All remote resources of this client are freed, but the server can continue
		/// to serve other clients.
		/// Calling <see cref="Dispose"/> afterwards is safe.
		/// </summary>
		public void Disconnect()
		{
			lock (_accessLock)
			{
				_messageHandler.PrintStats(Logger); // Dispose is too late, log earlier
				_instanceManager.PerformGc(_writer, true);
				int sequence = _interceptor.NextSequenceNumber();
				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.ClientDisconnecting, sequence);
				using (var lck = hd.WriteHeader(_writer))
				{
					_writer.Write(_instanceManager.ProcessIdentifier);
				}

				_disconnected = true;
			}
		}

		public void Dispose()
		{
			lock (_accessLock)
			{
				if (_server != null)
				{
					try
					{
						_instanceManager.PerformGc(_writer, true);
					}
					catch (Exception x) when (x is RemotingException || x is IOException)
					{
						// Ignore
					}

					_server.Terminate(_disconnected);
					_server.Dispose();
				}

				_server = null;

				_client?.Dispose();
				_interceptor.Dispose();
				_instanceManager.Clear();

				_client = null;
			}
		}

		public void ForceGc()
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			lock (_accessLock)
			{
				_instanceManager.PerformGc(_writer, false);
			}

			var remoteServerService = RequestRemoteInstance<IRemoteServerService>();
			if (remoteServerService != null)
			{
				remoteServerService.PerformGc();
			}
		}

		public Task ForceGcAsync()
		{
			return Task.Factory.StartNew(ForceGc);
		}
	}
}
