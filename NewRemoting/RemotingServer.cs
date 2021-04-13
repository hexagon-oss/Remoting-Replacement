using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;

#pragma warning disable SYSLIB0011
namespace NewRemoting
{
    public sealed class RemotingServer : IDisposable, IInternalClient
    {
        private int m_networkPort;
        private TcpListener m_listener;
        private bool m_threadRunning;
        private IFormatter m_formatter;
        private List<(Thread Thread, NetworkStream Stream)> m_threads;
        private Thread m_mainThread;
        private readonly ProxyGenerator _proxyGenerator;
        private TcpClient _returnChannel;
        private IInternalClient _realContainer;

        public RemotingServer(int networkPort)
        {
            _realContainer = new RealServerReferenceContainer();
            m_networkPort = networkPort;
            m_threads = new ();
            m_formatter = new BinaryFormatter();
            _proxyGenerator = new ProxyGenerator(new DefaultProxyBuilder());
            _returnChannel = null;
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver;
        }

        /// <summary>
        /// This ctor is used if this server runs on the client side (for the return channel). The actual data store is the local client instance.
        /// </summary>
        internal RemotingServer(int networkPort, IInternalClient replacedClient)
        {
            _realContainer = replacedClient;
            m_networkPort = networkPort;
            m_threads = new();
            m_formatter = new BinaryFormatter();
            _proxyGenerator = new ProxyGenerator(new DefaultProxyBuilder());
            _returnChannel = null;
        }

        public bool IsRunning => m_mainThread != null && m_mainThread.IsAlive;

        public int NetworkPort => m_networkPort;

        private Assembly AssemblyResolver(object sender, ResolveEventArgs args)
        {
            if (args.RequestingAssembly == null || args.RequestingAssembly.FullName != Assembly.GetExecutingAssembly().FullName)
            {
                // Only try to resolve assemblies that are loaded by us.
                return null;
            }

            string dllOnly = args.Name;
            int idx = dllOnly.IndexOf(',', StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                dllOnly = dllOnly.Substring(0, idx);
                dllOnly += ".dll";
            }
            string path = Path.GetDirectoryName(args.RequestingAssembly.Location);
            var assembly = Assembly.LoadFile(Path.Combine(path, dllOnly));
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

        public void ServerStreamHandler(object obj)
        {
            Stream stream = (Stream)obj;
            BinaryReader r = new BinaryReader(stream, Encoding.Unicode);
            BinaryWriter w = new BinaryWriter(stream, Encoding.Unicode);

            try
            {
                while (m_threadRunning)
                {
                    RemotingCallHeader hd = default;
                    if (!hd.ReadFrom(r))
                    {
                        throw new RemotingException("Incorrect data stream - sync lost", RemotingExceptionKind.ProtocolError);
                    }

                    if (hd.Function == RemotingFunctionType.OpenReverseChannel)
                    {
                        string clientIp = r.ReadString();
                        int clientPort = r.ReadInt32();
                        if (_returnChannel != null && _returnChannel.Connected)
                        {
                            continue;
                        }

                        _returnChannel = new TcpClient(clientIp, clientPort);
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
                        WriteArgumentToStream(m_formatter, w, newInstance);

                        continue;
                    }

                    List<Type> typeOfGenericArguments = new List<Type>();
                    for (int i = 0; i < methodGenericArgs; i++)
                    {
                        var typeName = r.ReadString();
                        var t = GetTypeFromAnyAssembly(typeName);
                        typeOfGenericArguments.Add(t);
                    }

                    int numArgs = r.ReadInt32();
                    object[] args = new object[numArgs];
                    for (int i = 0; i < numArgs; i++)
                    {
                        var decodedArg = ReadArgumentFromStream(m_formatter, r);
                        args[i] = decodedArg;
                    }

                    object realInstance = _realContainer.GetLocalInstanceFromReference(instance);

                    if (typeOfCaller == null)
                    {
                        typeOfCaller = realInstance.GetType();
                    }

                    var methods = typeOfCaller.GetMethods(BindingFlags.Instance | BindingFlags.Public);
                    MethodInfo me = methods.First(x => x.MetadataToken == methodNo);

                    if (me == null)
                    {
                        throw new RemotingException($"No such method: {methodNo}", RemotingExceptionKind.ProxyManagementError);
                    }

                    if (methodGenericArgs > 0)
                    {
                        me = me.MakeGenericMethod(typeOfGenericArguments.ToArray());
                    }

                    object returnValue = me.Invoke(realInstance, args);
                    RemotingCallHeader hdReturnValue = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
                    hdReturnValue.WriteTo(w);
                    if (me.ReturnType != typeof(void))
                    {
                        WriteArgumentToStream(m_formatter, w, returnValue);
                    }

                    int index = 0;
                    foreach (var byRefArguments in me.GetParameters())
                    {
                        if (byRefArguments.ParameterType.IsByRef)
                        {
                            WriteArgumentToStream(m_formatter, w, args[index]);
                        }

                        index++;
                    }
                }
            }
            catch (IOException x)
            {
                // Remote connection closed
                Console.WriteLine($"Server handler died due to {x}");
            }
        }

        public object ReadArgumentFromStream(IFormatter formatter, BinaryReader r)
        {
            RemotingReferenceType referenceType = (RemotingReferenceType)r.ReadInt32();
            if (referenceType == RemotingReferenceType.SerializedItem)
            {
                int argumentLen = r.ReadInt32();
                byte[] argumentData = r.ReadBytes(argumentLen);
                MemoryStream ms = new MemoryStream(argumentData, false);
                object decodedArg = formatter.Deserialize(ms);
                return decodedArg;
            }
            else if (referenceType == RemotingReferenceType.RemoteReference)
            {
                string objectId = r.ReadString();
                string typeName = r.ReadString();
                object obj = _realContainer.GetLocalInstanceFromReference(objectId);

                if (obj.GetType().FullName != typeName)
                {
                    throw new RemotingException("Expected type of argument was different from actual", RemotingExceptionKind.ProxyManagementError);
                }

                return obj;
            }
            else if (referenceType == RemotingReferenceType.NewProxy)
            {
                string objectId = r.ReadString();
                string typeName = r.ReadString();

                Type t = GetTypeFromAnyAssembly(typeName);

                object obj;
                if (t.IsInterface)
                {
                    obj = _proxyGenerator.CreateInterfaceProxyWithoutTarget(t, new ClientSideInterceptor(_returnChannel, this, _proxyGenerator, formatter));
                }
                else
                {
                    obj = _proxyGenerator.CreateClassProxy(t, t.GetInterfaces(), new ClientSideInterceptor(_returnChannel, this, _proxyGenerator, formatter));
                }

                _realContainer.AddKnownRemoteInstance(obj, objectId);

                return obj;
            }

            throw new RemotingException("Unsupported argument type declaration (neither reference nor instance)", RemotingExceptionKind.ProxyManagementError);
        }

        public void WriteArgumentToStream(IFormatter formatter, BinaryWriter w, object data)
        {
            Type t = data.GetType();
            if (t.IsSerializable)
            {
                MemoryStream ms = new MemoryStream();
                formatter.Serialize(ms, data);
                w.Write((int)RemotingReferenceType.SerializedItem);
                w.Write((int)ms.Length);
                byte[] array = ms.ToArray();
                w.Write(array, 0, (int)ms.Length);
            }
            else if (t.IsAssignableTo(typeof(MarshalByRefObject)))
            {
                string objectId = _realContainer.GetIdForLocalObject(data, out bool isNew);
                w.Write((int)(isNew ? RemotingReferenceType.NewProxy : RemotingReferenceType.RemoteReference));
                w.Write(data.GetType().AssemblyQualifiedName);
                w.Write(objectId);
            }
            else
            {
                throw new SerializationException($"Object {data} is neither serializable nor MarshalByRefObject");
            }
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

                Assembly ass = Assembly.Load(name);
                return ass.GetType(assemblyQualifiedName, true);
            }

            throw new TypeLoadException($"Type {assemblyQualifiedName} not found. No assembly specified");
        }

        public void ReceiverThread()
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

        public void AddKnownRemoteInstance(object obj, string objectId)
        {
            _realContainer.AddKnownRemoteInstance(obj, objectId);
        }

        public bool TryGetRemoteInstance(object obj, out string objectId)
        {
            return _realContainer.TryGetRemoteInstance(obj, out objectId);
        }

        public object GetLocalInstanceFromReference(string objectId)
        {
            return _realContainer.GetLocalInstanceFromReference(objectId);
        }

        public string GetIdForLocalObject(object obj, out bool isNew)
        {
            return _realContainer.GetIdForLocalObject(obj, out isNew);
        }
    }
}
