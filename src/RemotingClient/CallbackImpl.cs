using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SampleServerClasses;

namespace RemotingClient
{
	public class CallbackImpl : MarshalByRefObject, ICallbackInterface
	{
		public event Action<string> Callback;

		public virtual void FireSomeAction(string nameOfAction)
		{
			Console.WriteLine($"The server means that {nameOfAction}");
		}

		public void InvokeCallback(string data)
		{
			Callback?.Invoke(data);
		}
	}
}
