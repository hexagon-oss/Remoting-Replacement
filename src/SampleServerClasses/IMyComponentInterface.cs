using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public interface IMyComponentInterface
	{
		event Action<DateTime, string> TimeChanged;

		DateTime QueryTime();

		Stream GetRemoteStream(string fileName);

		void StartTiming(TimeSpan interval);

		void StopTiming();

		string ProcessName();

		string ConfiguredName();
	}
}
