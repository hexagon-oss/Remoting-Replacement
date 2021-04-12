using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
    internal interface IInternalClient
    {
        void AddKnownRemoteInstance(object obj, string objectId);

        bool TryGetRemoteInstance(object obj, out string objectId);

        object GetLocalInstanceFromReference(string objectId);

        string GetIdForLocalObject(object obj, out bool isNew);
    }
}
