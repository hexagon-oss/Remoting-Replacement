using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public interface IMarshallInterface
	{
		event Action AnEvent0;
		event Action<string> AnEvent1;
		event Action<string, string> AnEvent2;
		event Action<string, string, string> AnEvent3;
		event Action<string, string, string, string> AnEvent4;
		event Action<string, string, string, string, string> AnEvent5;

		string StringProcessId();
		void DoCallbackOnEvent(string msg);
		void DoCallbackOnEvent5(string msg);

		void CleanEvents();
		void RegisterForCallback(ICallbackInterface callbackInterface);
		void EnsureCallbackWasUsed();
		public void RegisterEvent(Action<int> progressFeedback);
		public void SetProgress(int progress);
	}
}
