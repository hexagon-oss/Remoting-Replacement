using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RemotingServer;
using SampleServerClasses;

namespace RemotingClient
{
	public class CallbackImpl : MarshalByRefObject, ICallbackInterface
	{
		public virtual void FireSomeAction(string nameOfAction)
		{
			Console.WriteLine($"The server means that {nameOfAction}");
		}
	}
}
