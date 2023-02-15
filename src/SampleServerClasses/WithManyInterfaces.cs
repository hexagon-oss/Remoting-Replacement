using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SampleServerClasses
{
	public class WithManyInterfaces : MarshalByRefObject, IMarshallInterface, IDisposable, IEnumerable<int>
	{
		public event Action AnEvent0;
		public event Action<string> AnEvent1;
		public event Action<string, string> AnEvent2;
		public event Action<string, string, string> AnEvent3;
		public event Action<string, string, string, string> AnEvent4;
		public event Action<string, string, string, string, string> AnEvent5;

		public string StringProcessId()
		{
			return "SomeString";
		}

		public void DoCallbackOnEvent(string msg)
		{
		}

		public void DoCallbackOnEvent5(string msg)
		{
			AnEvent5?.Invoke(msg, msg, msg, msg, msg);
		}

		public void CleanEvents()
		{
			AnEvent0 = null;
			AnEvent1 = null;
			AnEvent2 = null;
			AnEvent3 = null;
			AnEvent4 = null;
		}

		public void RegisterForCallback(ICallbackInterface callbackInterface)
		{
			throw new NotImplementedException();
		}

		public void EnsureCallbackWasUsed()
		{
			throw new NotImplementedException();
		}

		public void RegisterEvent(Action<int> progressFeedback)
		{
			throw new NotImplementedException();
		}

		public void SetProgress(int progress)
		{
			throw new NotImplementedException();
		}

		public void Dispose()
		{
		}

		public IEnumerator<int> GetEnumerator()
		{
			return new List<int>.Enumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public virtual void FireEvent()
		{
			var msg = "Fire";
			var name = "With Many interfaces";
			AnEvent4?.Invoke(msg, msg, msg, name);
			AnEvent3?.Invoke(msg, msg, name);
			AnEvent2?.Invoke(msg, name);
			AnEvent1?.Invoke(msg);
			AnEvent0?.Invoke();
		}
	}
}
