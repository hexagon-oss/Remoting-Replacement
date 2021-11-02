using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public interface IMarshallInterface
	{
		event Action<string> AnEvent;

		string StringProcessId();
		void DoCallbackOnEvent(string msg);
	}
}
