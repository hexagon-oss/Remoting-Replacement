using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    public enum RemotingFunctionType
    {
        None = 0,
        CreateInstanceWithDefaultCtor = 1,
        CreateInstance = 2,
        MethodCall = 3,
        MethodReply = 4,
    }
}
