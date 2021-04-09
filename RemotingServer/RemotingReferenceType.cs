using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    public enum RemotingReferenceType
    {
        Undefined = 0,
        SerializedItem = 1,
        RemoteReference = 2,
        NewProxy = 3,
    }
}
