using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public sealed class SealedClass : MarshalByRefObject, IDisposable, IMyComponentInterface
	{
		public event Action<DateTime, string> TimeChanged;
		public DateTime QueryTime()
		{
			return DateTime.Now;
		}

		public Stream GetRemoteStream(string fileName)
		{
			return null;
		}

		public void StartTiming(TimeSpan rate)
		{
			TimeChanged?.Invoke(DateTime.Now, "Test");
		}

		public void StopTiming()
		{
			throw new NotImplementedException();
		}

		public string ProcessName()
		{
			return Environment.ProcessPath;
		}

		public string ConfiguredName()
		{
			return "Unknown";
		}

		public void Dispose()
		{
		}
	}
}
