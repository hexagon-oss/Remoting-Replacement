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

#pragma warning disable SYSLIB0011
namespace NewRemoting
{
    public class RemotingServer
    {
        private int m_networkPort;
        private TcpListener m_listener;
        private bool m_threadRunning;
        // private ConditionalWeakTable<string, object> m_serverReferences;
        private Dictionary<string, object> m_serverHardReferences;
        private IFormatter m_formatter;
        private List<Thread> m_threads;
        private Thread m_mainThread;

        public RemotingServer(int networkPort)
        {
            m_networkPort = networkPort;
            m_threads = new List<Thread>();
            m_formatter = new BinaryFormatter();
            // m_serverReferences = new ConditionalWeakTable<string, object>();
            m_serverHardReferences = new Dictionary<string, object>();
        }

        private string AddObjectId(object obj, out bool isNewReference)
        {
            string unique = GetObjectInstanceId(obj);
            isNewReference = m_serverHardReferences.TryAdd(unique, obj);

            return unique;
        }

        public static string GetObjectInstanceId(object obj)
        {
            return FormattableString.Invariant($"{obj.GetType().FullName}-{RuntimeHelpers.GetHashCode(obj)}");
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
                        throw new InvalidDataException("Incorrect data stream - sync lost");
                    }

                    string instance = r.ReadString();
                    string method = r.ReadString();

                    if (hd.Function == RemotingFunctionType.CreateInstanceWithDefaultCtor)
                    {
                        // CreateInstance call, instance is just a type in this case
                        Type t = GetTypeFromAnyAssembly(instance);
                        object newInstance = Activator.CreateInstance(t, false);
                        RemotingCallHeader hdReturnValue1 = new RemotingCallHeader(RemotingFunctionType.MethodReply, hd.Sequence);
                        hdReturnValue1.WriteTo(w);
                        WriteArgumentToStream(m_formatter, w, newInstance);

                        continue;
                    }

                    int numArgs = r.ReadInt32();
                    object[] args = new object[numArgs];
                    for (int i = 0; i < numArgs; i++)
                    {
                        var decodedArg = ReadArgumentFromStream(m_formatter, r);
                        args[i] = decodedArg;
                    }

                    if (!m_serverHardReferences.TryGetValue(instance, out object realInstance))
                    {
                        throw new MethodAccessException($"No such remote instance: {instance}");
                    }

                    // TODO: Extend to include argument types
                    MethodInfo me = realInstance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public);

                    if (me == null)
                    {
                        throw new MethodAccessException($"No such method: {method}");
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
            catch (IOException)
            {
                // Remote connection closed
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
                if (!m_serverHardReferences.TryGetValue(objectId, out object obj) || obj.GetType().FullName != typeName)
                {
                    throw new SerializationException("There's no instance with this ID");
                }

                return obj;
            }
            else if (referenceType == RemotingReferenceType.NewProxy)
            {
                string objectId = r.ReadString();
                string typeName = r.ReadString();
                if (m_serverHardReferences.TryGetValue(objectId, out object obj))
                {
                    throw new SerializationException("Server instance with this ID already exists");
                }

                Type t = GetTypeFromAnyAssembly(typeName);



                return obj;
            }

            throw new NotSupportedException("Unexpected argument type");
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
                string objectId = AddObjectId(data, out bool isNew);
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
                string assemblyName = assemblyQualifiedName.Substring(idx + 1);
                Assembly ass = Assembly.Load(assemblyName);
                return ass.GetType(assemblyQualifiedName, true);
            }

            throw new TypeLoadException($"Type {assemblyQualifiedName} not found. No assembly specified");
        }

        public void ReceiverThread()
        {
            while (m_threadRunning)
            {
                var tcpClient = m_listener.AcceptTcpClient();
                var stream = tcpClient.GetStream();
                Thread ts = new Thread(ServerStreamHandler);
                m_threads.Add(ts);
                ts.Start(stream);
            }
        }

        public void Terminate()
        {
            m_threadRunning = false;
            m_listener.Stop();
            m_mainThread.Join();
        }
    }
}
