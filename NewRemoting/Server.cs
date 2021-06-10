using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.Components.DictionaryAdapter;
using Castle.DynamicProxy;

#pragma warning disable SYSLIB0011
namespace NewRemoting
{
	public sealed class Server : IDisposable
	{
		public const string ServerExecutableName = "RemotingServer.exe";

		private int m_networkPort;
		private TcpListener m_listener;
		private bool m_threadRunning;
		private readonly IFormatter m_formatter;
		private readonly List<(Thread Thread, NetworkStream Stream)> m_threads;
		private Thread m_mainThread;
		private readonly ProxyGenerator _proxyGenerator;
		private TcpClient _returnChannel;
		private readonly CancellationTokenSource _terminatingSource = new CancellationTokenSource();
		private readonly object _channelWriterLock = new object();
		private readonly ConcurrentDictionary<string, Assembly> _knownAssemblies = new ();

		/// <summary>
		/// This is the interceptor for calls from the server to the client using a server-side proxy (i.e. an interface registered for a callback)
		/// </summary>
		private ClientSideInterceptor _serverInterceptorForCallbacks;

		private readonly InstanceManager _instanceManager;
		private readonly MessageHandler _messageHandler;

		public Server(int networkPort)
		{
			m_networkPort = networkPort;
			m_threads = new();
			m_formatter = new BinaryFormatter();
			_proxyGenerator = new ProxyGenerator(new DefaultProxyBuilder());
			_returnChannel = null;
			_instanceManager = new InstanceManager();

			// This instance will only be finally initialized once the return channel is opened
			_messageHandler = new MessageHandler(_instanceManager, _proxyGenerator, m_formatter);
			AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver;
		}

		/// <summary>
		/// This ctor is used if this server runs on the client side (for the return channel). The actual data store is the local client instance.
		/// </summary>
		internal Server(int networkPort, MessageHandler messageHandler, ClientSideInterceptor localInterceptor)
		{
			m_networkPort = networkPort;
			_messageHandler = messageHandler;
			_instanceManager = messageHandler.InstanceManager;
			messageHandler.Init(localInterceptor);
			m_threads = new();
			m_formatter = new BinaryFormatter();
			_proxyGenerator = new ProxyGenerator(new DefaultProxyBuilder());
			_returnChannel = null;
			_serverInterceptorForCallbacks = localInterceptor;
		}

		public bool IsRunning => m_mainThread != null && m_mainThread.IsAlive;

		public int NetworkPort => m_networkPort;

		public static Process StartLocalServerProcess()
		{
			return Process.Start("RemotingServer.exe");
		}

		private Assembly AssemblyResolver(object sender, ResolveEventArgs args)
		{
			Debug.WriteLine($"Attempting to resolve {args.Name} from {args.RequestingAssembly}");
			if (args.RequestingAssembly == null)
			{
				return null;
			}

			/* AssemblyLoadContext ctx = AssemblyLoadContext.GetLoadContext(args.RequestingAssembly);
			try
			{
				AssemblyName name = new AssemblyName(args.Name);
				Assembly loaded = ctx.LoadFromAssemblyName(name);
				return loaded;
			}
			catch (Exception x)
			{
				Debug.WriteLine(x.ToString());
			}
			*/
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
					Debug.WriteLine(x.ToString());
				}
			}

			var sourceReferences = args.RequestingAssembly.GetReferencedAssemblies();
			var name2 = sourceReferences.FirstOrDefault(x => x.Name == args.Name);
			if (name2 != null)
			{
				Assembly loaded = Assembly.Load(name2);
				_knownAssemblies.AddOrUpdate(args.Name, loaded, (s, assembly1) => loaded);
				return loaded;
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
				var potentialEntries = Directory.EnumerateFileSystemEntries(startDirectory, dllOnly, SearchOption.AllDirectories).ToArray();
				if (potentialEntries.Length == 1)
				{
					path = potentialEntries[0];
				}
			}

			var assembly = Assembly.LoadFile(path);
			_knownAssemblies.AddOrUpdate(args.Name, assembly, (s, assembly1) => assembly);
			return assembly;
		}

		public void StartListening()
		{
			m_listener = new TcpListener(IPAddress.Any, m_networkPort);
			m_threadRunning = true;
			m_listener.Start();
			m_mainThread = new Thread(ReceiverThread);
			m_mainThread.Start();
		}

		private void ServerStreamHandler(object obj)
		{
			Stream stream = (Stream)obj;
			BinaryReader r = new BinaryReader(stream, Encoding.Unicode);
			BinaryWriter w = new BinaryWriter(stream, Encoding.Unicode);

			List<Task> openTasks = new List<Task>();

			try
			{
				while (m_threadRunning)
				{
					// Clean up task list
					for (var index = 0; index < openTasks.Count; index++)
					{
						var task = openTasks[index];
						if (task.Exception != null)
						{
							throw new RemotingException("Unhandled task exception in remote server", RemotingExceptionKind.UnsupportedOperation);
						}
						if (task.IsCompleted)
						{
							openTasks.Remove(task);
							index--;
						}
					}

					RemotingCallHeader hd = default;
					if (!hd.ReadFrom(r))
					{
						throw new RemotingException("Incorrect data stream - sync lost", RemotingExceptionKind.ProtocolError);
					}

					if (ExecuteCommand(hd, r))
					{
						continue;
					}

					string instance = r.ReadString();
					string typeOfCallerName = r.ReadString();
					Type typeOfCaller = null;
					if (!string.IsNullOrEmpty(typeOfCallerName))
					{
						typeOfCaller = GetTypeFromAnyAssembly(typeOfCallerName);
					}

					int methodNo = r.ReadInt32(); // token of method to call
					int methodGenericArgs = r.ReadInt32(); // number of generic arguments of method (not generic arguments of declaring class!)

					if (hd.Function == RemotingFunctionType.CreateInstanceWithDefaultCtor)
					{
						// CreateInstance call, instance is just a type in this case
						if (methodGenericArgs != 0)
						{
							throw new RemotingException("Constructors cannot have generic arguments", RemotingExceptionKind.UnsupportedOperation);
						}

						Type t = GetTypeFromAnyAssembly(instance);
						object newInstance = Activator.CreateInstance(t, false);
						RemotingCallHeader hdReturnValue1 = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
						hdReturnValue1.WriteTo(w);
						_messageHandler.WriteArgumentToStream(w, newInstance);

						continue;
					}

					if (hd.Function == RemotingFunctionType.CreateInstance)
					{
						// CreateInstance call, instance is just a type in this case
						if (methodGenericArgs != 0)
						{
							throw new RemotingException("Constructors cannot have generic arguments", RemotingExceptionKind.UnsupportedOperation);
						}

						int numArguments = r.ReadInt32();
						object[] ctorArgs = new object[numArguments];
						for (int i = 0; i < ctorArgs.Length; i++)
						{
							// Constructors are selected dynamically on the server side (below), therefore we can't pass the argument type here.
							// This may disallow calling a constructor with a client-side reference. It is yet to clarify whether that's a problem or not.
							ctorArgs[i] = _messageHandler.ReadArgumentFromStream(r, null, false, null);
						}

						Type t = GetTypeFromAnyAssembly(instance);
						object newInstance = Activator.CreateInstance(t, ctorArgs);
						RemotingCallHeader hdReturnValue1 = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
						hdReturnValue1.WriteTo(w);
						_messageHandler.WriteArgumentToStream(w, newInstance);

						continue;
					}

					if (hd.Function == RemotingFunctionType.RequestServiceReference)
					{
						Type t = GetTypeFromAnyAssembly(instance);
						object newInstance = ServiceContainer.GetService(t);
						RemotingCallHeader hdReturnValue1 = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
						hdReturnValue1.WriteTo(w);
						_messageHandler.WriteArgumentToStream(w, newInstance);
						continue;
					}

					List<Type> typeOfGenericArguments = new List<Type>();
					for (int i = 0; i < methodGenericArgs; i++)
					{
						var typeName = r.ReadString();
						var t = GetTypeFromAnyAssembly(typeName);
						typeOfGenericArguments.Add(t);
					}
					
					object realInstance = _instanceManager.GetObjectFromId(instance);

					if (typeOfCaller == null)
					{
						typeOfCaller = realInstance.GetType();
					}

					var methods = typeOfCaller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo me = methods.First(x => x.MetadataToken == methodNo);

					if (me == null)
					{
						throw new RemotingException($"No such method: {methodNo}", RemotingExceptionKind.ProxyManagementError);
					}

					if (methodGenericArgs > 0)
					{
						me = me.MakeGenericMethod(typeOfGenericArguments.ToArray());
					}

					int numArgs = r.ReadInt32();
					object[] args = new object[numArgs];
					for (int i = 0; i < numArgs; i++)
					{
						var decodedArg = _messageHandler.ReadArgumentFromStream(r, null, false, me.GetParameters()[i].ParameterType);
						args[i] = decodedArg;
					}

					openTasks.Add(Task.Run(() =>
					{
						InvokeRealInstance(me, realInstance, args, hd, w);
					}));
				}
			}
			catch (IOException x)
			{
				// Remote connection closed
				Console.WriteLine($"Server handler died due to {x}");
			}
		}

		private void InvokeRealInstance(MethodInfo me, object realInstance, object[] args, RemotingCallHeader hd, BinaryWriter w)
		{
			// Here, the actual target method of the proxied call is invoked.
			Debug.WriteLine($"MainServer: Invoking {me}, sequence {hd.Sequence}");
			object returnValue;
			try
			{
				if (realInstance == null && !me.IsStatic)
				{
					throw new NullReferenceException("Cannot invoke on a non-static method without an instance");
				}
				if (realInstance is Delegate del)
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
				throw new NotImplementedException("This should wrap the exception to the caller", x);
			}

			// Ensure only one thread writes data to the stream at a time (to keep the data from one message together)
			lock (_channelWriterLock)
			{
				RemotingCallHeader hdReturnValue = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
				hdReturnValue.WriteTo(w);
				if (me.ReturnType != typeof(void))
				{
					if (returnValue == null)
					{
						Debug.WriteLine($"MainServer: {hd.Sequence} reply is null");
					}
					else
					{
						Debug.WriteLine($"MainServer: {hd.Sequence} reply is of type {returnValue.GetType()}");
					}
					_messageHandler.WriteArgumentToStream(w, returnValue);
				}

				int index = 0;
				foreach (var byRefArguments in me.GetParameters())
				{
					if (byRefArguments.ParameterType.IsByRef)
					{
						_messageHandler.WriteArgumentToStream(w, args[index]);
					}

					index++;
				}
			}
		}

		private bool ExecuteCommand(RemotingCallHeader hd, BinaryReader r)
		{
			if (hd.Function == RemotingFunctionType.OpenReverseChannel)
			{
				string clientIp = r.ReadString();
				int clientPort = r.ReadInt32();
				if (_returnChannel != null && _returnChannel.Connected)
				{
					return true;
				}

				_returnChannel = new TcpClient(clientIp, clientPort);
				_serverInterceptorForCallbacks = new ClientSideInterceptor("Server", _returnChannel, _messageHandler);
				_messageHandler.Init(_serverInterceptorForCallbacks);
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
					Debug.WriteLine(x.ToString());
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

		public static Type GetTypeFromAnyAssembly(string assemblyQualifiedName)
		{
			Type t = Type.GetType(assemblyQualifiedName);
			if (t != null)
			{
				return t;
			}

			int idx = assemblyQualifiedName.IndexOf(',', StringComparison.OrdinalIgnoreCase);
			if (idx > 0)
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
				catch (FileNotFoundException x)
				{
					Debug.WriteLine(x.ToString());
					// Try the legacy way
					Assembly ass = Assembly.LoadFrom(name.Name + ".dll");
					return ass.GetType(typeName, true);
				}
			}

			throw new TypeLoadException($"Type {assemblyQualifiedName} not found. No assembly specified");
		}

		private void ReceiverThread()
		{
			while (m_threadRunning)
			{
				try
				{
					var tcpClient = m_listener.AcceptTcpClient();
					var stream = tcpClient.GetStream();
					Thread ts = new Thread(ServerStreamHandler);
					m_threads.Add((ts, stream));
					ts.Start(stream);
				}
				catch (SocketException x)
				{
					Console.WriteLine($"Server terminating? Got {x}");
				}
			}
		}

		public void Terminate()
		{
			_returnChannel?.Dispose();
			_returnChannel = null;

			m_threadRunning = false;
			foreach (var thread in m_threads)
			{
				thread.Stream.Close();
				thread.Thread.Join();
			}

			m_threads.Clear();
			m_listener?.Stop();
			m_mainThread?.Join();
		}

		public void Dispose()
		{
			Terminate();
		}

		public void WaitForTermination()
		{
			_terminatingSource.Token.WaitHandle.WaitOne();
		}
	}
}
