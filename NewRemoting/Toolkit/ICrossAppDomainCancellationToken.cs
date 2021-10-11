using System.Threading;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// <see cref="CancellationTokenSource" /> is not remoting capable.
	/// The idea is to have the token source as server object and just pass a proxy to the client.
	/// Each app domain itself can spawn a local cancellation token but cancellation can be requested outside the current app domain.
	/// </summary>
	public interface ICrossAppDomainCancellationToken
	{
		/// <summary>
		/// <see cref="CancellationTokenSource.IsCancellationRequested"/>
		/// </summary>
		bool IsCancellationRequested
		{
			get;
		}

		/// <summary>
		/// Provides a token which is usable in the current app domain and cannot be transferred to another app domain.
		/// Since <see cref="CancellationToken"/> is not remoting capable.
		/// </summary>
		CancellationToken LocalToken
		{
			get;
		}
	}
}
