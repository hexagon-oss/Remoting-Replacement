using System.Net.Sockets;

namespace NewRemoting.Toolkit
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
