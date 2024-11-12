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
		/// <summary>
		/// Waits until any of the WaitHandles becomes available
		/// </summary>
		/// <param name="handles">List of handles</param>
		/// <param name="timeoutMs">Timeout in milliseconds</param>
		/// <returns>The index of the handle that triggered, or <see cref="WaitHandle.WaitTimeout"/>.</returns>
		public static int WaitAny(this IList<WaitHandle> handles, int timeoutMs)
		{
			return WaitAny(handles, TimeSpan.FromMilliseconds(timeoutMs));
		}

		/// <summary>
		/// Waits until any of the WaitHandles becomes available
		/// </summary>
		/// <param name="handles">List of handles</param>
		/// <param name="timeout">Timeout</param>
		/// <returns>The index of the handle that triggered, or <see cref="WaitHandle.WaitTimeout"/>.</returns>
		public static int WaitAny(this IList<WaitHandle> handles, TimeSpan timeout)
		{
			Stopwatch sw = Stopwatch.StartNew();
			do
			{
				for (var index = 0; index < handles.Count; index++)
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

		/// <summary>
		/// Waits until all handles are signaled
		/// </summary>
		/// <param name="handles">List of handles</param>
		/// <param name="timeoutMs">Timeout in milliseconds</param>
		/// <returns>True if all handles where signaled, false if the timeout has elapsed</returns>
		public static bool WaitAll(this IList<WaitHandle> handles, int timeoutMs)
		{
			return WaitAll(handles, TimeSpan.FromMilliseconds(timeoutMs));
		}

		/// <summary>
		/// Waits until all handles are signaled
		/// </summary>
		/// <param name="handles">List of handles</param>
		/// <param name="timeout">Timeout</param>
		/// <returns>True if all handles where signaled, false if the timeout has elapsed</returns>
		public static bool WaitAll(this IList<WaitHandle> handles, TimeSpan timeout)
		{
			Stopwatch sw = Stopwatch.StartNew();
			do
			{
				bool allAvailable = true;
				for (var index = 0; index < handles.Count; index++)
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
