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

namespace RemotingServer
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

        private string AddObjectId(object obj, bool hardReference = false)
        {
            string unique = GetObjectInstanceId(obj);
            // m_serverReferences.AddOrUpdate(unique, obj);
            if (hardReference)
            {
                m_serverHardReferences[unique] = obj;
            }

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
                        Type t = Type.GetType(instance, true);
                        object newInstance = Activator.CreateInstance(t, false);
                        AddObjectId(newInstance, true);
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
                }
            }
            catch (IOException x)
            {
                // Remote connection closed
            }
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
                string objectId = r.ReadString();
                string typeName = r.ReadString();
                if (!m_serverHardReferences.TryGetValue(objectId, out object obj) || obj.GetType().FullName != typeName)
                {
                    throw new SerializationException("There's no instance with this ID");
                }

                return obj;
            }
        }

        public void WriteArgumentToStream(IFormatter formatter, BinaryWriter w, object data)
        {
            Type t = data.GetType();
            if (t.IsSerializable)
            {
                MemoryStream ms = new MemoryStream();
                formatter.Serialize(ms, data);
                w.Write(true);
                w.Write((int)ms.Length);
                byte[] array = ms.ToArray();
                w.Write(array, 0, (int)ms.Length);
            }
            else if (t.IsAssignableTo(typeof(MarshalByRefObject)))
            {
                string objectId = AddObjectId(data, true);
                w.Write(false);
                w.Write(data.GetType().FullName);
                w.Write(objectId);
            }
            else
            {
                throw new SerializationException($"Object {data} is neither serializable nor MarshalByRefObject");
            }
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
