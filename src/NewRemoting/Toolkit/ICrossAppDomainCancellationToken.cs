using System;
using System.Threading;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// <see cref="CancellationTokenSource" /> is not remoting capable.
	/// This interface is remoting-capable though and can be used instead.
	/// </summary>
	public interface ICrossAppDomainCancellationToken
	{
		/// <summary>
		/// <see cref="CancellationTokenSource.IsCancellationRequested"/>
		/// </summary>
		bool IsCancellationRequested { get; }

		/// <summary>
		/// Throws a <see cref="OperationCanceledException"/> when <see cref="IsCancellationRequested"/> is true.
		/// </summary>
		void ThrowIfCancellationRequested();

		void Register(Action action);
	}
}
