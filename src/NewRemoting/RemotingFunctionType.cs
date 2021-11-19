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
		ShutdownServer, // The client requests the server to terminate
		ServerShuttingDown, // The server shuts down and requests the client to disconnect
		ClientDisconnecting, // The client is disconnecting but will not take the server down
		RequestServiceReference,
		LoadClientAssemblyIntoServer,
		ExceptionReturn,
		GcCleanup,
	}
}
