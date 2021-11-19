using System;
using System.Threading;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// Remoting capable version of <see cref="CancellationTokenSource"/>
	/// </summary>
	public interface ICrossAppDomainCancellationTokenSource : IDisposable, ICrossAppDomainCancellationToken
	{
		/// <summary>
		/// <see cref="CancellationTokenSource.Cancel()"/>
		/// </summary>
		void Cancel();
	}
}
