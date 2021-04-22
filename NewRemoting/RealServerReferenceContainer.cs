using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
    internal class RealServerReferenceContainer : IInternalClient
    {
        private ConditionalWeakTable<object, string> m_clientReferences;
        private Dictionary<string, object> m_serverHardReferences;
        private object _internalLock;

        public RealServerReferenceContainer()
        {
            _internalLock = new object();
            m_clientReferences = new();
            m_serverHardReferences = new Dictionary<string, object>();
        }

        public object CommunicationLinkLock => _internalLock;

        public static string GetObjectInstanceId(object obj)
        {
            string objectReference = FormattableString.Invariant($"{obj.GetType().FullName}-{RuntimeHelpers.GetHashCode(obj)}");
            Console.WriteLine($"Created object reference with id {objectReference}");
            return objectReference;
        }

        internal string AddObjectId(object obj, out bool isNewReference)
        {
            string unique = GetObjectInstanceId(obj);
            isNewReference = m_serverHardReferences.TryAdd(unique, obj);

            return unique;
        }

        public void AddKnownRemoteInstance(object obj, string objectId)
        {
            m_clientReferences.AddOrUpdate(obj, objectId);
        }

        public bool TryGetRemoteInstance(object obj, out string objectId)
        {
            return m_clientReferences.TryGetValue(obj, out objectId);
        }

        public object GetLocalInstanceFromReference(string objectId)
        {
            if (!m_serverHardReferences.TryGetValue(objectId, out var obj))
            {
                throw new NotSupportedException($"There's no local instance for reference {objectId}");
            }

            return obj;
        }

        public bool TryGetLocalInstanceFromReference(string objectId, out object obj)
        {
            if (m_serverHardReferences.TryGetValue(objectId, out obj))
            {
                return true;
            }

            return false;
        }

        public string GetIdForLocalObject(object obj, out bool isNew)
        {
            return AddObjectId(obj, out isNew);
        }
    }
}
