using System.Net.Sockets;
using System.Runtime.Remoting;

namespace NewRemoting
{
	internal class InvokerRemoteAware : IWeakEventInvoker
	{
		public bool InvokeTarget(WeakEventEntry target, object[] arguments)
		{
			bool removalRequired = true;
			try
			{
				removalRequired = !target.Invoke(arguments);
			}
			catch (RemotingException)
			{
				target.Remove();
			}
			catch (SocketException)
			{
				target.Remove();
			}

			return removalRequired;
		}

	}
}
