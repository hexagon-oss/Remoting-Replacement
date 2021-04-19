using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
    public enum RemotingFunctionType
    {
        None = 0,
        CreateInstanceWithDefaultCtor,
        CreateInstance,
        MethodCall,
        MethodReply,
        OpenReverseChannel,
        ShutdownServer
    }
}
