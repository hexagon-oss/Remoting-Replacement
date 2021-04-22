using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
    public enum RemotingReferenceType
    {
        Undefined = 0,
        SerializedItem = 1,
        RemoteReference = 2,
        MethodPointer = 4,
    }
}
