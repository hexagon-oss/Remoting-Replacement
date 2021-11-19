using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;

namespace NewRemoting.Toolkit
{
	public static class PingUtil
	{
		public static bool CancellableTryWaitForPingFailure(string remoteHost, TimeSpan timeout, CancellationToken cancellationToken)
		{
			return CancellableTryWaitForPingResult(remoteHost, timeout, cancellationToken, false, 0);
		}

		public static bool CancellableTryWaitForPingResponse(string remoteHost, TimeSpan timeout, CancellationToken cancellationToken)
		{
			return CancellableTryWaitForPingResult(remoteHost, timeout, cancellationToken, true, 0);
		}

		public static bool CancellableTryWaitForPingResponse(IPAddress ipAddress, TimeSpan timeout, CancellationToken cancellationToken)
		{
			return CancellableTryWaitForPingResponse(ipAddress.ToString(), timeout, cancellationToken);
		}

		public static bool CancellableTryWaitForPingFailure(IPAddress ipAddress, TimeSpan timeout, CancellationToken cancellationToken)
		{
			return CancellableTryWaitForPingFailure(ipAddress.ToString(), timeout, cancellationToken);
		}

		public static bool CancellableTryWaitForPingFailure(string remoteHost, TimeSpan timeout, CancellationToken cancellationToken, int retriesOnPingTimeout)
		{
			return CancellableTryWaitForPingResult(remoteHost, timeout, cancellationToken, false, retriesOnPingTimeout);
		}

		public static bool CancellableTryWaitForPingResponse(string remoteHost, TimeSpan timeout, CancellationToken cancellationToken, int retriesOnPingTimeout)
		{
			return CancellableTryWaitForPingResult(remoteHost, timeout, cancellationToken, true, retriesOnPingTimeout);
		}

		public static bool CancellableTryWaitForPingResponse(IPAddress ipAddress, TimeSpan timeout, CancellationToken cancellationToken, int retriesOnPingTimeout)
		{
			return CancellableTryWaitForPingResponse(ipAddress.ToString(), timeout, cancellationToken, retriesOnPingTimeout);
		}

		public static bool CancellableTryWaitForPingFailure(IPAddress ipAddress, TimeSpan timeout, CancellationToken cancellationToken, int retriesOnPingTimeout)
		{
			return CancellableTryWaitForPingFailure(ipAddress.ToString(), timeout, cancellationToken, retriesOnPingTimeout);
		}

		/// <summary>
		/// A ping signal is sent periodically, if not answered within a certain time
		/// it is tried again until the supplied timeout is elapsed.
		/// If only try to send once the remote host might not be ready and
		/// the ping times out even if the remote host becomes available later (within the timeout).
		/// </summary>
		/// <returns>True if ping was successfull false if ping timouted</returns>
		/// <exception cref="OperationCanceledException">Thrown when operation was canceled</exception>
		/// <exception cref="ArgumentException">The supplied host string is invalid</exception>
		internal static bool CancellableTryWaitForPingResult(string remoteHost, TimeSpan timeout, CancellationToken cancellationToken, bool expectSuccess, int retriesOnPingTimeout)
		{
			var gotResult = false;
			var pingCompletedEvent = new ManualResetEventSlim(false);
			var ping = new Ping();
			PingCompletedEventArgs completedArgs = null;
			ping.PingCompleted += (sender, args) =>
			{
				completedArgs = args;
				pingCompletedEvent.Set();
			};

			var pingTimeOutCounter = 0;
			var timeoutCheck = Stopwatch.StartNew();
			// Check for timeout or cancellation
			while (timeoutCheck.Elapsed < timeout)
			{
				cancellationToken.ThrowIfCancellationRequested();
				pingCompletedEvent.Reset();
				var timeoutForException = Timeout.Infinite;
				try
				{
					// Send ping, if timeout value is too small host has no time to answer even if it would
					ping.SendAsync(remoteHost, (int)TimeSpan.FromSeconds(1).TotalMilliseconds, null);
				}
				catch (PingException)
				{
					// catch ping exception and wait a little before trying again
					timeoutForException = 100;
				}

				// Wait for result or cancellation
				var waitResult = WaitHandle.WaitAny(new[] { cancellationToken.WaitHandle, pingCompletedEvent.WaitHandle }, timeoutForException);
				// Stop waiting on failure
				if (waitResult == 1)
				{
					// A timeout might happen even if the remote host is still available :
					// - if the remote host is busy
					// - because ping sends data via UDP
					// -> do some retries / ignore a number of consecutive timeouts
					if (completedArgs.Reply != null && completedArgs.Reply.Status == IPStatus.TimedOut && pingTimeOutCounter < retriesOnPingTimeout)
					{
						pingTimeOutCounter++;
						continue;
					}

					// Reset ping timeout counter
					pingTimeOutCounter = 0;

					// Check expected result
					if (IsResultAsExpected(completedArgs, expectSuccess))
					{
						gotResult = true;
						break;
					}
				}

				if (waitResult != WaitHandle.WaitTimeout)
				{
					// Cancel pending ping request
					ping.SendAsyncCancel();
				}
			}

			return gotResult;
		}

		private static bool IsResultAsExpected(PingCompletedEventArgs completedEventArgs, bool expectSuccess)
		{
			if (expectSuccess && completedEventArgs.Reply != null && completedEventArgs.Reply.Status == IPStatus.Success)
			{
				return true;
			}

			if (!expectSuccess && (completedEventArgs.Reply == null || completedEventArgs.Reply.Status != IPStatus.Success))
			{
				return true;
			}

			return false;
		}
	}
}
