using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable SYSLIB0011
namespace NewRemoting
{
	/// <summary>
	/// Create a remoting server.
	/// A remoting server is listening on a network port for commands from other processes and computers.
	/// It is intended behavior that this allows a remote process to execute arbitrary code within this process, including
	/// code that is uploaded to the server from the client. No security checks nor authentication is currently used,
	/// so USE ONLY IN TRUSTED NETWORKS!
	/// </summary>
	public sealed class Server : IDisposable
	{
		/// <summary>
		/// the certificate
		/// </summary>
		private static X509Certificate _serverCertificate;

		public const string ServerExecutableName = "RemotingServer.exe";
		private const string RuntimeVersionRegex = "(\\d+\\.\\d)";
		internal const int AuthenticationSucceededToken = 567223;

		private readonly List<ThreadData> _threads;
		private readonly ProxyGenerator _proxyGenerator;
		private readonly CancellationTokenSource _terminatingSource = new CancellationTokenSource();
		private readonly object _channelWriterLock = new object();
		private readonly ConcurrentDictionary<string, Assembly> _knownAssemblies = new();
		private readonly InstanceManager _instanceManager;
		private readonly MessageHandler _messageHandler;
		private readonly FormatterFactory _formatterFactory;
		private readonly AuthenticationInformation _authenticationInformation;

		/// <summary>
		/// This is the interceptor list for calls from the server to the client using a server-side proxy (i.e. an interface registered for a callback)
		/// There is one instance per client.
		/// </summary>
		private readonly Dictionary<string, ClientSideInterceptor> _serverInterceptorForCallbacks;

		private readonly object _interceptorLock;

		/// <summary>
		/// This contains a (typically small) queue of streams that will be used for the reverse communication.
		/// The list is emptied by OpenReverseChannel commands.
		/// </summary>
		private readonly ConcurrentDictionary<int, Stream> _clientStreamsExpectingUse;

		private Thread _mainThread;
		private int _networkPort;
		private TcpListener _listener;
		private bool _threadRunning;

		/// <summary>
		/// This contains the stream that the client class has already opened for its own server.
		/// If this is non-null, this server instance lives within the client.
		/// </summary>
		private Stream _preopenedStream;

		/// <summary>
		/// Create a remoting server instance. Other processes (local or remote) will be able to perform remote calls to this process.
		/// Start <see cref="StartListening"/> to actually start the server.
		/// </summary>
		/// <param name="networkPort">Network port to open server on</param>
		/// <param name="authenticationInformation">the certificate filename, and password</param>
		/// <param name="logger">Optional logger instance, for debugging purposes</param>
		public Server(int networkPort, AuthenticationInformation authenticationInformation, ILogger logger = null)
		{
			_authenticationInformation = authenticationInformation;
			Logger = logger ?? NullLogger.Instance;
			_interceptorLock = new object();
			_networkPort = networkPort;
			_threads = new();
			_proxyGenerator = new ProxyGenerator(new DefaultProxyBuilder());
			_preopenedStream = null;
			_clientStreamsExpectingUse = new();
			_serverInterceptorForCallbacks = new();

			_instanceManager = new InstanceManager(_proxyGenerator, Logger);
			_formatterFactory = new FormatterFactory(_instanceManager);
			KillProcessWhenChannelDisconnected = false;

			// This instance will only be finally initialized once the return channel is opened
			_messageHandler = new MessageHandler(_instanceManager, _formatterFactory, Logger);
			AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver;
			RegisterStandardServices();
		}

		/// <summary>
		/// This ctor is used if this server runs on the client side (for the return channel). The actual data store is the local client instance.
		/// </summary>
		internal Server(Stream preopenedStream, MessageHandler messageHandler, ClientSideInterceptor localInterceptor, string certificateFilename = null, string certificatePassword = null, ILogger logger = null)
		{
			_networkPort = 0;
			_interceptorLock = new object();
			Logger = logger ?? NullLogger.Instance;
			_preopenedStream = preopenedStream;
			_clientStreamsExpectingUse = null; // Shall not be used in this case
			_messageHandler = messageHandler;
			_instanceManager = messageHandler.InstanceManager;

			_threads = new();
			_formatterFactory = new FormatterFactory(_instanceManager);
			_proxyGenerator = new ProxyGenerator(new DefaultProxyBuilder());
			_serverInterceptorForCallbacks = new Dictionary<string, ClientSideInterceptor>() { { localInterceptor.OtherSideProcessId, localInterceptor } };

			// We need the resolver also on the client side, since the server might request the client to load an assembly
			AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver;
		}

		/// <summary>
		/// If true, the server process is killed when the client disconnects unexpectedly.
		/// Use with caution, especially if not running as standalone server.
		/// </summary>
		public bool KillProcessWhenChannelDisconnected
		{
			get;
			set;
		}

		public bool IsRunning => _mainThread != null && _mainThread.IsAlive;

		public ILogger Logger { get; }
		public int NetworkPort => _networkPort;

		public static Process StartLocalServerProcess()
		{
			return Process.Start("RemotingServer.exe");
		}

		private void RegisterStandardServices()
		{
			// Register standard services
			if (ServiceContainer.GetService<IRemoteServerService>() == null)
			{
				ServiceContainer.AddService(typeof(IRemoteServerService), new RemoteServerService(this, Logger));
			}
		}

		private Assembly AssemblyResolver(object sender, ResolveEventArgs args)
		{
			Logger.Log(LogLevel.Debug, $"Attempting to resolve {args.Name} from {args.RequestingAssembly}");
			if (args.RequestingAssembly == null)
			{
				return null;
			}

			bool found = _knownAssemblies.TryGetValue(args.Name, out var a);
			if (found && a != null)
			{
				return a;
			}

			_knownAssemblies.TryAdd(args.Name, null);
			if (!found)
			{
				AssemblyName name = new AssemblyName(args.Name);
				// Don't try this twice with the same assembly - causes a stack overflow
				try
				{
					// Try using the default context
					Assembly loaded = Assembly.Load(name);
					_knownAssemblies.AddOrUpdate(args.Name, loaded, (s, assembly1) => loaded);
					return loaded;
				}
				catch (Exception x)
				{
					Logger.Log(LogLevel.Error, x.ToString());
				}
			}

			var sourceReferences = args.RequestingAssembly.GetReferencedAssemblies();
			var name2 = sourceReferences.FirstOrDefault(x => x.Name == args.Name);
			if (name2 != null)
			{
				try
				{
					Assembly loaded = Assembly.Load(name2);
					_knownAssemblies.AddOrUpdate(args.Name, loaded, (s, assembly1) => loaded);
					return loaded;
				}
				catch (Exception x)
				{
					Logger.Log(LogLevel.Error, x.ToString());
				}
			}

			string dllOnly = args.Name;
			int idx = dllOnly.IndexOf(',', StringComparison.OrdinalIgnoreCase);
			if (idx >= 0)
			{
				dllOnly = dllOnly.Substring(0, idx);
				dllOnly += ".dll";
			}

			string currentDirectory = Path.GetDirectoryName(args.RequestingAssembly.Location);
			string path = Path.Combine(currentDirectory, dllOnly);

			if (!File.Exists(path))
			{
				return null;
			}

			// If we find a file with the same name in the "runtimes" subfolder, we should probably pick that one instead of the one in the main directory.
			string osName = string.Empty;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				osName = "win";
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				osName = "unix";
			}

			string startDirectory = Path.Combine(currentDirectory, "runtimes", osName);
			if (Directory.Exists(startDirectory))
			{
				var potentialEntries = Directory
					.EnumerateFileSystemEntries(startDirectory, dllOnly, SearchOption.AllDirectories).ToArray();
				if (potentialEntries.Length > 0)
				{
					GetBestRuntimeDll(potentialEntries, ref path);
				}
			}

			try
			{
				var assembly = Assembly.LoadFile(path);
				_knownAssemblies.AddOrUpdate(args.Name, assembly, (s, assembly1) => assembly);
				return assembly;
			}
			catch (Exception x)
			{
				Logger.Log(LogLevel.Error, x.ToString());
				return null;
			}
		}

		internal static void GetBestRuntimeDll(string[] potentialEntries, ref string path)
		{
			if (potentialEntries.Length == 1)
			{
				path = potentialEntries[0];
			}
			else if (potentialEntries.Length > 1)
			{
				// Multiple files with this name exist in runtimes. Take the newest.
				float bestVersion = -1;
				int bestIndex = 0;
				for (var index = 0; index < potentialEntries.Length; index++)
				{
					var entry = potentialEntries[index];
					var matches = Regex.Matches(entry, RuntimeVersionRegex);
					if (matches.Any())
					{
						string version = matches[0].Value;
						if (float.TryParse(version, NumberStyles.Any, CultureInfo.InvariantCulture, out float thisVersion) && thisVersion > bestVersion)
						{
							bestIndex = index;
							bestVersion = thisVersion;
						}
					}
				}

				// This will in either case return an entry, let's hope it's the right one if the above fails
				path = potentialEntries[bestIndex];
			}
		}

		internal static SslStream Authenticate(TcpClient client, X509Certificate certificate, ILogger logger)
		{
			SslStream sslStream = new SslStream(client.GetStream(), false);
			try
			{
				SslServerAuthenticationOptions options = new SslServerAuthenticationOptions();
				// we require the client authentication
				options.ClientCertificateRequired = true;
				// When using certificates, the system validates that the client certificate is not revoked,
				// by checking that the client certificate is not in the revoked certificate list.
				// we do not check for revocation of the client certificate
				options.CertificateRevocationCheckMode = X509RevocationMode.NoCheck;
				options.RemoteCertificateValidationCallback = new RemoteCertificateValidationCallback(ValidateClientCertificate);
				options.ServerCertificate = certificate;
				sslStream.AuthenticateAsServer(options);
				return sslStream;
			}
			catch (AuthenticationException e)
			{
				logger.Log(LogLevel.Error, e.ToString());
				logger.Log(LogLevel.Error, "Authentication failed - closing the connection.");
				sslStream.Close();
				client.Close();
				throw;
			}
		}

		private static bool ValidateClientCertificate(object sender, X509Certificate remoteCertificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			// we do not check the sslPolicyErrors because we do not require the client to install the certificate
			if (remoteCertificate != null && remoteCertificate.ToString() == _serverCertificate.ToString())
			{
				return true;
			}

			return false;
		}

		public void StartListening()
		{
			if (_authenticationInformation != null && !string.IsNullOrEmpty(_authenticationInformation.CertificateFileName) && !string.IsNullOrEmpty(_authenticationInformation.CertificatePassword))
			{
				_serverCertificate = new X509Certificate2(_authenticationInformation.CertificateFileName, _authenticationInformation.CertificatePassword, X509KeyStorageFlags.MachineKeySet);
			}

			_listener = new TcpListener(IPAddress.Any, _networkPort);
			_threadRunning = true;
			_listener.Start();
			_mainThread = new Thread(ReceiverThread);
			_mainThread.Start();
		}

		/// <summary>
		/// Starts the server stream handler directly
		/// </summary>
		internal void StartProcessing(string otherSideProcessId)
		{
			if (_preopenedStream == null)
			{
				throw new InvalidOperationException($"{nameof(StartProcessing)} must only be called for the client's own server");
			}

			_threadRunning = true;
			Thread ts = new Thread(ServerStreamHandler);
			ts.Name = "Server stream handler for client side";
			var td = new ThreadData(ts, _preopenedStream, new BinaryReader(_preopenedStream, MessageHandler.DefaultStringEncoding), otherSideProcessId, new ConnectionSettings());
			_threads.Add(td);
			ts.Start(td);
		}

		private void ServerStreamHandler(object obj)
		{
			ThreadData td = (ThreadData)obj;
			var stream = td.Stream;
			var r = td.BinaryReader;

			List<Task> openTasks = new List<Task>();

			try
			{
				while (_threadRunning)
				{
					// Clean up task list
					for (var index = 0; index < openTasks.Count; index++)
					{
						var task = openTasks[index];
						if (task.Exception != null)
						{
							throw new RemotingException($"Unhandled task exception in remote server: {task.Exception.Message}", task.Exception);
						}

						if (task.IsCompleted)
						{
							openTasks.Remove(task);
							index--;
						}
					}

					RemotingCallHeader hd = new RemotingCallHeader();
					if (!hd.ReadFrom(r))
					{
						throw new RemotingException("Incorrect data stream - sync lost");
					}

					if (ExecuteCommand(hd, r, td, out bool clientDisconnecting))
					{
						if (clientDisconnecting)
						{
							Logger.Log(LogLevel.Error, $"Server handler disconnecting upon request.");
							// Just close the stream and return. Do NOT throw an exception.
							stream.Dispose();
							return;
						}

						continue;
					}

					string instance = r.ReadString();
					string typeOfCallerName = r.ReadString();
					Type typeOfCaller = null;
					if (!string.IsNullOrEmpty(typeOfCallerName))
					{
						typeOfCaller = GetTypeFromAnyAssembly(typeOfCallerName);
					}

					string methodId = r.ReadString(); // identifier of method to call
					int methodGenericArgs = r.ReadInt32(); // number of generic arguments of method (not generic arguments of declaring class!)

					MemoryStream ms = new MemoryStream(4096);
					BinaryWriter answerWriter = new BinaryWriter(ms, MessageHandler.DefaultStringEncoding);

					if (hd.Function == RemotingFunctionType.CreateInstanceWithDefaultCtor)
					{
						// CreateInstance call, instance is just a type in this case
						if (methodGenericArgs != 0)
						{
							throw new RemotingException("Constructors cannot have generic arguments");
						}

						Type t = null;
						if (!TryReflectionMethod(() => GetTypeFromAnyAssembly(instance), answerWriter, hd.Sequence, td.OtherSideProcessId, out t))
						{
							SendAnswer(td, ms);
							continue;
						}

						if (!TryReflectionMethod(() => Activator.CreateInstance(t, false), answerWriter, hd.Sequence, td.OtherSideProcessId, out var newInstance))
						{
							continue;
						}

						RemotingCallHeader hdReturnValue1 = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
						hdReturnValue1.WriteHeaderNoLock(answerWriter);
						// Can this fail? Typically, this is a reference type, so it shouldn't.
						_messageHandler.WriteArgumentToStream(answerWriter, newInstance, td.OtherSideProcessId, td.Settings);

						SendAnswer(td, ms);

						continue;
					}

					if (hd.Function == RemotingFunctionType.CreateInstance)
					{
						// CreateInstance call, instance is just a type in this case
						if (methodGenericArgs != 0)
						{
							throw new RemotingException("Constructors cannot have generic arguments");
						}

						int numArguments = r.ReadInt32();
						object[] ctorArgs = new object[numArguments];
						for (int i = 0; i < ctorArgs.Length; i++)
						{
							// Constructors are selected dynamically on the server side (below), therefore we can't pass the argument type here.
							// This may disallow calling a constructor with a client-side reference. It is yet to clarify whether that's a problem or not.
							ctorArgs[i] = _messageHandler.ReadArgumentFromStream(r, null, null, false, null, td.OtherSideProcessId, td.Settings);
						}

						Type t = null;
						if (!TryReflectionMethod(() => GetTypeFromAnyAssembly(instance), answerWriter, hd.Sequence, td.OtherSideProcessId, out t))
						{
							SendAnswer(td, ms);
							continue;
						}

						if (!TryReflectionMethod(() => Activator.CreateInstance(t, ctorArgs), answerWriter, hd.Sequence, td.OtherSideProcessId, out var newInstance))
						{
							SendAnswer(td, ms);
							continue;
						}

						RemotingCallHeader hdReturnValue1 = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
						hdReturnValue1.WriteHeaderNoLock(answerWriter);
						_messageHandler.WriteArgumentToStream(answerWriter, newInstance, td.OtherSideProcessId, td.Settings);

						SendAnswer(td, ms);
						continue;
					}

					if (hd.Function == RemotingFunctionType.RequestServiceReference)
					{
						Type t = null;
						if (!TryReflectionMethod(() => GetTypeFromAnyAssembly(instance), answerWriter, hd.Sequence, td.OtherSideProcessId, out t))
						{
							SendAnswer(td, ms);
							continue;
						}

						object newInstance = ServiceContainer.GetService(t);
						RemotingCallHeader hdReturnValue1 = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
						hdReturnValue1.WriteHeaderNoLock(answerWriter);
						_messageHandler.WriteArgumentToStream(answerWriter, newInstance, td.OtherSideProcessId, td.Settings);

						SendAnswer(td, ms);
						continue;
					}

					List<Type> typeOfGenericArguments = new List<Type>();
					for (int i = 0; i < methodGenericArgs; i++)
					{
						var typeName = r.ReadString();
						var t = GetTypeFromAnyAssembly(typeName);
						typeOfGenericArguments.Add(t);
					}

					object realInstance = _instanceManager.GetObjectFromId(instance, typeOfCallerName, methodId, out bool wasDelegateTarget);

					if (typeOfCaller == null && wasDelegateTarget == false)
					{
						typeOfCaller = realInstance.GetType();
					}

					var allMethods = typeOfCaller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					IEnumerable<MethodInfo> methods;
					if (methodGenericArgs > 0)
					{
						// If the method has generic arguments, only methods with the same number of args can match
						methods = allMethods.Where(x => x.GetGenericArguments().Length == methodGenericArgs);
					}
					else
					{
						methods = allMethods.Where(x => x.IsGenericMethod == false);
					}

					MethodInfo methodToCall = null;
					foreach (var me in methods)
					{
						var me2 = me;
						try
						{
							if (methodGenericArgs > 0)
							{
								me2 = me.MakeGenericMethod(typeOfGenericArguments.ToArray());
							}
						}
						catch (Exception x) when (x is InvalidOperationException || x is ArgumentException)
						{
							// The generic arguments don't match the method. This can't be the one we're looking for
							continue;
						}

						if (InstanceManager.GetMethodIdentifier(me2) == methodId)
						{
							methodToCall = me2;
							break;
						}
					}

					if (methodToCall == null)
					{
						throw new RemotingException($"Unable to find a method with id {methodId}");
					}

					int numArgs = r.ReadInt32();
					object[] args = new object[numArgs];
					for (int i = 0; i < numArgs; i++)
					{
						var decodedArg = _messageHandler.ReadArgumentFromStream(r, methodToCall, null, false, methodToCall.GetParameters()[i].ParameterType, td.OtherSideProcessId, td.Settings);
						args[i] = decodedArg;
					}

					openTasks.Add(Task.Run(() =>
					{
						try
						{
							InvokeRealInstance(methodToCall, realInstance, args, hd, answerWriter, td, wasDelegateTarget);
						}
						catch (SerializationException x)
						{
							// The above throws if serializing the return value(s) fails (return type of method call not serializable)
							// Clear the stream, we must only send the exception
							ms.Position = 0;
							ms.SetLength(0);
							_messageHandler.SendExceptionReply(x, answerWriter, hd.Sequence, td.OtherSideProcessId);
						}

						SendAnswer(td, ms);
					}));
				}

				Logger.Log(LogLevel.Information, $"Server on port {NetworkPort} exited");
			}
			catch (Exception x) when (x is IOException || x is ObjectDisposedException)
			{
				// Remote connection closed
				Logger.Log(LogLevel.Error, $"Server handler died due to {x}");
				if (KillProcessWhenChannelDisconnected)
				{
					_terminatingSource.Cancel();
				}
			}
		}

		private void SendAnswer(ThreadData thread, MemoryStream rawDataMessage)
		{
			lock (thread)
			{
				rawDataMessage.Position = 0;
				try
				{
					rawDataMessage.CopyTo(thread.Stream);
				}
				catch (ObjectDisposedException x)
				{
					// Ignore
					Logger.LogWarning(x, $"It appears the connection is closed: {x.Message}");
				}

				rawDataMessage.Dispose();
			}
		}

		/// <summary>
		/// Calls the given operator, returning its result.
		/// If the function throws an exception related to reflection (TypeLoadException or similar), the exception is sent back over the stream
		/// </summary>
		/// <typeparam name="T">The return type of the operation</typeparam>
		/// <param name="operation">Whatever needs to be done</param>
		/// <param name="writer">Binary writer to send the exception back</param>
		/// <param name="sequenceId">Sequence Id of the call</param>
		/// <param name="otherSideProcessId">The process that instantiated the call</param>
		/// <param name="result">[Out] Result of the operation</param>
		/// <returns>True on success, false if an exception occurred</returns>
		private bool TryReflectionMethod<T>(Func<T> operation, BinaryWriter writer, int sequenceId, string otherSideProcessId, out T result)
		{
			T ret = default;
			try
			{
				ret = operation();
			}
			catch (Exception x) when (x is TypeLoadException || x is FileNotFoundException || x is TargetInvocationException)
			{
				_messageHandler.SendExceptionReply(x, writer, sequenceId, otherSideProcessId);
				result = default;
				return false;
			}

			result = ret;
			return true;
		}

		private void InvokeRealInstance(MethodInfo me, object realInstance, object[] args, RemotingCallHeader hd, BinaryWriter w, ThreadData td, bool wasDelegateTarget)
		{
			// Here, the actual target method of the proxied call is invoked.
			Logger.Log(LogLevel.Debug, $"MainServer: Invoking {me}, sequence {hd.Sequence}");
			object returnValue;
			try
			{
				if (wasDelegateTarget)
				{
					try
					{
						// Just return nothing, as we can't perform the call
						if (me.ReturnType == typeof(void) || !me.ReturnType.IsValueType)
						{
							returnValue = null;
						}
						else
						{
							returnValue = Activator.CreateInstance(me.ReturnType);
						}
					}
					catch (Exception x)
					{
						Logger.LogError(x, $"Could not fire event on callback method, and {me.ReturnType} cannot be instantiated");
						throw new InvalidOperationException($"Could not fire event on callback method, and {me.ReturnType} cannot be instantiated", x);
					}
				}
				else if (realInstance == null && !me.IsStatic)
				{
					throw new NullReferenceException("Cannot invoke on a non-static method without an instance");
				}
				else if (realInstance is Delegate del)
				{
					returnValue = me.Invoke(del.Target, args);
				}
				else
				{
					returnValue = me.Invoke(realInstance, args);
				}
			}
			catch (Exception x)
			{
				Logger.Log(LogLevel.Debug, $"Invoking threw {x}");
				lock (_channelWriterLock)
				{
					if (x.InnerException != null)
					{
						// the exception we want to forward is here the inner exception
						x = x.InnerException;
					}

					_messageHandler.SendExceptionReply(x, w, hd.Sequence, td.OtherSideProcessId);
				}

				return;
			}

			// Ensure only one thread writes data to the stream at a time (to keep the data from one message together)
			lock (_channelWriterLock)
			{
				RemotingCallHeader hdReturnValue = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
				hdReturnValue.WriteHeaderNoLock(w);
				// Return the result of a call: The return value and any ref or out parameter values
				if (me.ReturnType != typeof(void))
				{
					if (returnValue == null)
					{
						Logger.Log(LogLevel.Debug, $"MainServer: {hd.Sequence} reply is null");
					}
					else
					{
						Logger.Log(LogLevel.Debug, $"MainServer: {hd.Sequence} reply is of type {returnValue.GetType()}");
					}

					_messageHandler.WriteArgumentToStream(w, returnValue, td.OtherSideProcessId, td.Settings);
				}

				int index = 0;
				foreach (var byRefArguments in me.GetParameters())
				{
					// Hack to make sure the contents of the array argument in calls to Stream.Read(byte[], int, int) are marshalled both ways
					if (byRefArguments.ParameterType.IsArray && me.DeclaringType != null && me.DeclaringType.IsSubclassOf(typeof(Stream)))
					{
						_messageHandler.WriteArgumentToStream(w, args[index], td.OtherSideProcessId, td.Settings);
					}
					else if (byRefArguments.ParameterType.IsByRef)
					{
						_messageHandler.WriteArgumentToStream(w, args[index], td.OtherSideProcessId, td.Settings);
					}

					index++;
				}
			}
		}

		private bool ExecuteCommand(RemotingCallHeader hd, BinaryReader r, ThreadData td, out bool clientDisconnecting)
		{
			clientDisconnecting = false;
			if (hd.Function == RemotingFunctionType.OpenReverseChannel)
			{
				string clientIp = r.ReadString();
				int clientPort = r.ReadInt32();
				string initialOtherSideProcessId = r.ReadString();
				int connectionIdentifier = r.ReadInt32();
				Stream streamToUse;
				while (!_clientStreamsExpectingUse.TryGetValue(connectionIdentifier, out streamToUse))
				{
					// Wait until the matching connection is available - when several clients connect simultaneously, they may interfere here
					Thread.Sleep(20);
				}

				var newInterceptor = new ClientSideInterceptor(initialOtherSideProcessId, _instanceManager.ProcessIdentifier, false, td.Settings, streamToUse, _messageHandler, Logger);

				newInterceptor.Start();
				_messageHandler.AddInterceptor(newInterceptor);
				_instanceManager.AddInterceptor(newInterceptor);
				lock (_interceptorLock)
				{
					_serverInterceptorForCallbacks.Add(initialOtherSideProcessId, newInterceptor);
				}

				return true;
			}

			if (hd.Function == RemotingFunctionType.ClientDisconnecting)
			{
				string clientName = r.ReadString();
				lock (_interceptorLock)
				{
					if (_serverInterceptorForCallbacks.TryGetValue(clientName, out var interceptor))
					{
						interceptor.Dispose();
					}
				}

				clientDisconnecting = true;
				return true;
			}

			if (hd.Function == RemotingFunctionType.LoadClientAssemblyIntoServer)
			{
				string assemblyName = r.ReadString();
				AssemblyName name = new AssemblyName(assemblyName);
				try
				{
					var assembly = Assembly.Load(name);
					_knownAssemblies.TryAdd(name.FullName, assembly);
				}
				catch (Exception x)
				{
					Logger.Log(LogLevel.Error, x.ToString());
				}

				return true;
			}

			if (hd.Function == RemotingFunctionType.GcCleanup)
			{
				int cnt = r.ReadInt32(); // Number of elements that follow
				for (int i = 0; i < cnt; i++)
				{
					string objectId = r.ReadString();
					_instanceManager.Remove(objectId, td.OtherSideProcessId, false);
				}

				return true;
			}

			if (hd.Function == RemotingFunctionType.ShutdownServer)
			{
				_terminatingSource.Cancel();
				return true;
			}

			return false;
		}

		public static Type GetTypeFromAnyAssembly(string assemblyQualifiedName, bool throwOnError = true)
		{
			Type t = Type.GetType(assemblyQualifiedName, false);
			if (t != null)
			{
				return t;
			}

			int start = 0;
			if (assemblyQualifiedName.Contains("]"))
			{
				// If the type contains a generic argument, its type is embedded in square brackets. We want the assembly of the final type, though
				start = assemblyQualifiedName.LastIndexOf("]", StringComparison.OrdinalIgnoreCase);
			}

			int idx = assemblyQualifiedName.IndexOf(',', start);
			if (idx > 0)
			{
				try
				{
					string assemblyName = assemblyQualifiedName.Substring(idx + 2);
					AssemblyName name = new AssemblyName(assemblyName);

					string typeName = assemblyQualifiedName.Substring(0, idx);

					try
					{
						Assembly ass = Assembly.Load(name);
						try
						{
							return ass.GetType(assemblyQualifiedName, true);
						}
						catch (ArgumentException)
						{
							return ass.GetType(typeName, true);
						}
					}
					catch (FileNotFoundException)
					{
						// Try the legacy way
						Assembly ass = Assembly.LoadFrom(name.Name + ".dll");
						return ass.GetType(typeName, true);
					}
				}
				catch (Exception e) when (!(e is NullReferenceException))
				{
					if (throwOnError)
					{
						throw new TypeLoadException($"Unable to load type {assemblyQualifiedName}. Probably cannot find the assembly it's defined in", e);
					}
					else
					{
						return null;
					}
				}
			}

			if (throwOnError)
			{
				throw new TypeLoadException($"Type {assemblyQualifiedName} not found. No assembly specified");
			}
			else
			{
				return null;
			}
		}

		/// <summary>
		/// Logs to both console and logger - useful for connection sequence
		/// </summary>
		private void LogToBoth(string msg, LogLevel level)
		{
			Console.WriteLine(msg);
			Logger.Log(level, msg);
		}

		/// <summary>
		/// This controls the master Socket.Accept thread.
		/// </summary>
		private void ReceiverThread()
		{
			while (_threadRunning)
			{
				try
				{
					var tcpClient = _listener.AcceptTcpClient();
					Stream stream = tcpClient.GetStream();
					LogToBoth("Server accepted client stream", LogLevel.Information);

					if (_serverCertificate != null)
					{
						LogToBoth("Server authentication started", LogLevel.Information);
						stream = Authenticate(tcpClient, _serverCertificate, Logger);
						LogToBoth("Server authentication done", LogLevel.Information);
					}

					using var reader = new BinaryReader(stream, MessageHandler.DefaultStringEncoding, true);

					byte[] authenticationToken = new byte[100];

					// TODO: This needs some kind of authentication / trust management, but I have _no_ clue on how to get that safely done,
					// since we (by design) want anyone to be able to connect and execute arbitrary code locally. Bad combination...
					var bytesRead = reader.Read(authenticationToken, 0, 100);
					if (bytesRead != 100)
					{
						LogToBoth(
							FormattableString.Invariant(
								$"Server disconnecting from client, could not read complete auth token (only {bytesRead})"),
							LogLevel.Error);
						tcpClient.Dispose();
						continue;
					}

					byte[] length = new byte[4];
					if (reader.Read(length, 0, 4) != 4)
					{
						LogToBoth(
							FormattableString.Invariant(
								$"Server disconnecting from client, could not read message length"), LogLevel.Error);
						tcpClient.Dispose();
						continue;
					}

					byte[] data = new byte[BitConverter.ToInt32(length)];

					bytesRead = reader.Read(data, 0, data.Length);
					if (bytesRead != data.Length)
					{
						LogToBoth(
							FormattableString.Invariant(
								$"Server disconnecting from client, could not read data length (only {bytesRead} of {data.Length})"),
							LogLevel.Error);
						tcpClient.Dispose();
						continue;
					}

					LogToBoth("Server received client authentication", LogLevel.Information);

					string otherSideInstanceId = MessageHandler.DefaultStringEncoding.GetString(data);
					int instanceHash = BitConverter.ToInt32(authenticationToken, 1);

					SendAuthenticationReply(stream);
					LogToBoth("Connected authenticated", LogLevel.Information);

					if (authenticationToken[0] == 1)
					{
						// first stream connected, wait until second stream is connected as well before starting server
						// just restart loop, but keep stream
						_clientStreamsExpectingUse.TryAdd(instanceHash, stream);
						continue;
					}

					bool interfaceOnlyClient = authenticationToken[5] == 1;
					ConnectionSettings settings = new ConnectionSettings()
					{
						InterfaceOnlyClient = interfaceOnlyClient,
					};

					Thread ts = new Thread(ServerStreamHandler);
					ts.Name = $"Remote server client {tcpClient.Client.RemoteEndPoint}";
					var td = new ThreadData(ts, stream, new BinaryReader(stream, MessageHandler.DefaultStringEncoding),
						otherSideInstanceId, settings);
					_threads.Add(td);
					ts.Start(td);

					LogToBoth("Server thread started", LogLevel.Information);
					Logger.LogInformation("Server thread started");
				}
				catch (SocketException x)
				{
					LogToBoth($"Server terminating? Got {x}", LogLevel.Error);
				}
				catch (AuthenticationException e)
				{
					LogToBoth($"Connection failed to authenticate. Got {e}", LogLevel.Error);
				}
				catch (System.IO.IOException e)
				{
					// can happen if the client certificate does not correspond to the server one (RemoteCertificateNameMismatch)
					// error on the client validate function
					LogToBoth($"Decryption failed. Got {e}", LogLevel.Error);
				}
			}
		}

		private void SendAuthenticationReply(Stream stream)
		{
			byte[] successToken = BitConverter.GetBytes(AuthenticationSucceededToken);
			stream.Write(successToken);

			var instanceIdentifier = MessageHandler.DefaultStringEncoding.GetBytes(_instanceManager.ProcessIdentifier);
			var lenBytes = BitConverter.GetBytes(instanceIdentifier.Length);
			stream.Write(lenBytes);
			stream.Write(instanceIdentifier);
		}

		public void PerformGc()
		{
			lock (_interceptorLock)
			{
				foreach (var i in _serverInterceptorForCallbacks)
				{
					i.Value.InitiateGc();
				}
			}
		}

		/// <summary>
		/// Terminate a link
		/// </summary>
		/// <param name="disconnected">True if the client has already logically disconnected</param>
		public void Terminate(bool disconnected)
		{
			_messageHandler.PrintStats();
			if (!disconnected)
			{
				MemoryStream ms = new MemoryStream();
				BinaryWriter binaryWriter = new BinaryWriter(ms, MessageHandler.DefaultStringEncoding);
				RemotingCallHeader hdReturnValue = new RemotingCallHeader(RemotingFunctionType.ServerShuttingDown, 0);
				hdReturnValue.WriteHeaderNoLock(binaryWriter);
				foreach (var thread in _threads)
				{
					try
					{
						SendAnswer(thread, ms);
					}
					catch (Exception x) when (x is IOException || x is ObjectDisposedException)
					{
						Logger.LogInformation(x, $"Sending termination command to client {thread.Thread.Name} failed. Ignoring.");
					}
				}
			}

			_preopenedStream?.Dispose();
			_preopenedStream = null;

			_threadRunning = false;
			foreach (var thread in _threads)
			{
				thread.Stream.Close();
				thread.BinaryReader.Dispose();
				thread.Thread.Join();
			}

			_threads.Clear();

			lock (_interceptorLock)
			{
				foreach (var clientThread in _serverInterceptorForCallbacks)
				{
					clientThread.Value.Dispose();
				}

				_serverInterceptorForCallbacks.Clear();
			}

			_listener?.Stop();
			_mainThread?.Join();
		}

		public void Dispose()
		{
			Terminate(false);
		}

		public void WaitForTermination()
		{
			_terminatingSource.Token.WaitHandle.WaitOne();
		}

		private sealed class ThreadData
		{
			public ThreadData(Thread thread, Stream stream, BinaryReader binaryReader, string otherSideProcessId, ConnectionSettings settings)
			{
				Thread = thread;
				Stream = stream;
				BinaryReader = binaryReader;
				OtherSideProcessId = otherSideProcessId;
				Settings = settings;
			}

			public Thread Thread { get; }
			public Stream Stream { get; }
			public BinaryReader BinaryReader { get; }

			public string OtherSideProcessId { get; }
			public ConnectionSettings Settings { get; }
		}
	}
}
