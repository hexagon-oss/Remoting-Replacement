using System;
using System.Threading;

namespace NewRemoting.Toolkit
{
	internal sealed class CrossAppDomainCancellationTokenSource : MarshalByRefObject, ICrossAppDomainCancellationTokenSource, ICrossAppDomainCancellationToken
	{
		private readonly CancellationTokenSource _cancellationTokenSource;

		public CrossAppDomainCancellationTokenSource()
		{
			_cancellationTokenSource = new CancellationTokenSource();
		}

		bool ICrossAppDomainCancellationToken.IsCancellationRequested
		{
			get
			{
				return _cancellationTokenSource.IsCancellationRequested;
			}
		}

		CancellationToken ICrossAppDomainCancellationToken.LocalToken
		{
			get
			{
				return _cancellationTokenSource.Token;
			}
		}

		public void Dispose()
		{
			_cancellationTokenSource.Dispose();
		}

		void ICrossAppDomainCancellationTokenSource.Cancel()
		{
			_cancellationTokenSource.Cancel();
		}
	}
}
