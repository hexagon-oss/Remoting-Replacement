using System;
using System.Threading;

namespace NewRemoting.Toolkit
{
	public class CrossAppDomainCancellationTokenSource : MarshalByRefObject, IDisposable
	{
		private readonly CancellationTokenSource _cancellationTokenSource;
		private long _cancellationTime;

		public CrossAppDomainCancellationTokenSource()
		{
			_cancellationTokenSource = new CancellationTokenSource();
			_cancellationTime = -1;
		}

		public CrossAppDomainCancellationTokenSource(TimeSpan timeout)
		{
			if (timeout < TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(timeout));
			}

			_cancellationTokenSource = new CancellationTokenSource();
			_cancellationTime = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
		}

		public virtual event Action OnCancellation;

		public virtual bool IsCancellationRequested
		{
			get
			{
				if (_cancellationTime >= 0 && Environment.TickCount64 >= _cancellationTime)
				{
					Cancel();
					return true;
				}

				return _cancellationTokenSource.IsCancellationRequested;
			}
		}

		public virtual ICrossAppDomainCancellationToken Token
		{
			get
			{
				return new CrossAppDomainCancellationToken(this);
			}
		}

		public void Dispose()
		{
			_cancellationTokenSource.Dispose();
		}

		public virtual void Cancel()
		{
			_cancellationTokenSource.Cancel();
			OnCancellation?.Invoke();
		}

		/// <summary>
		/// Cancel the operation after the time has elapsed.
		/// Setting a negative value disables cancellation after the previously set time.
		/// Calling this a second time with a positive timespan sets a new timeout, starting from now.
		/// </summary>
		/// <param name="timeSpan">The time after which the operation should be cancelled</param>
		public virtual void CancelAfter(TimeSpan timeSpan)
		{
			if (timeSpan < TimeSpan.Zero)
			{
				_cancellationTime = -1;
				return;
			}

			_cancellationTime = Environment.TickCount64 + (long)timeSpan.TotalMilliseconds;
		}

		private sealed class CrossAppDomainCancellationToken : MarshalByRefObject, ICrossAppDomainCancellationToken
		{
			private readonly CrossAppDomainCancellationTokenSource _source;

			/// <summary>
			/// Constructor for remote instance
			/// </summary>
			public CrossAppDomainCancellationToken()
			{
				_source = null;
			}

			public CrossAppDomainCancellationToken(CrossAppDomainCancellationTokenSource source)
			{
				_source = source;
			}

			public bool IsCancellationRequested => _source.IsCancellationRequested;

			public void ThrowIfCancellationRequested()
			{
				if (IsCancellationRequested)
				{
					throw new OperationCanceledException();
				}
			}

			/// <summary>
			/// Register the given action to be called on cancellation.
			/// The method is not marked virtual because this class is sealed. However, the
			/// method must only be called trough the interface/proxy.
			/// </summary>
			/// <param name="operation">Operation to perform</param>
			public void Register(Action operation)
			{
				if (operation == null)
				{
					throw new ArgumentNullException(nameof(operation));
				}

				_source.OnCancellation += () =>
				{
					operation();
				};
			}
		}
	}
}
