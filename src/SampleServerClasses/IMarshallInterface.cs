using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public interface IMarshallInterface
	{
		event Action<string, string> AnEvent;

		string StringProcessId();
		void DoCallbackOnEvent(string msg);

		void CleanEvents();
		void RegisterForCallback(ICallbackInterface callbackInterface);
		void EnsureCallbackWasUsed();
		public void RegisterEvent(Action<int> progressFeedback);
		public void SetProgress(int progress);
	}
}
