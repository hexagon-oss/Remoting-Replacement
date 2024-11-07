using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// A class providing extensions that support WaitAny and WaitAll in a remoting-compatible way for WaitHandles.
	/// </summary>
	public static class RemoteWaitHandle
	{
		public static int WaitAny(WaitHandle[] handles, TimeSpan timeout)
		{
			Stopwatch sw = Stopwatch.StartNew();
			do
			{
				for (var index = 0; index < handles.Length; index++)
				{
					var h = handles[index];
					if (h.WaitOne(0))
					{
						return index;
					}
				}

				Thread.Sleep(10);
			}
			while (sw.Elapsed < timeout);

			return WaitHandle.WaitTimeout;
		}

		public static bool WaitAll(WaitHandle[] handles, TimeSpan timeout)
		{
			Stopwatch sw = Stopwatch.StartNew();
			do
			{
				bool allAvailable = true;
				for (var index = 0; index < handles.Length; index++)
				{
					var h = handles[index];
					if (!h.WaitOne(0))
					{
						allAvailable = false;
						Thread.Sleep(10);
						break;
					}
				}

				if (allAvailable)
				{
					return true;
				}
			}
			while (sw.Elapsed < timeout);

			return false;
		}
	}
}
