using System;

namespace SampleServerClasses
{
	public interface ICallbackInterface
	{
		event Action<string> Callback;

		void FireSomeAction(string nameOfAction);

		void InvokeCallback(string data);
	}
}
