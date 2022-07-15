using System;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

namespace NewRemoting.Toolkit
{
	public class CrossAppDomainCancellationTokenSource : MarshalByRefObject, IDisposable
	{
		private readonly CancellationTokenSource _cancellationTokenSource;
		private Timer _timer;
		private bool _timerFired;

		public CrossAppDomainCancellationTokenSource()
		{
			_cancellationTokenSource = new CancellationTokenSource();
			_timer = null;
			_timerFired = false;
		}

		public CrossAppDomainCancellationTokenSource(TimeSpan timeout)
		{
			if (timeout < TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(timeout));
			}

			_cancellationTokenSource = new CancellationTokenSource();
			_timerFired = false;

			InitTimer(timeout);
		}

		public static ICrossAppDomainCancellationToken None
		{
			get
			{
				return new CrossAppDomainCancellationToken();
			}
		}

		private void InitTimer(TimeSpan timeout)
		{
			_timer = new Timer(timeout.TotalMilliseconds);
			_timer.AutoReset = false;
			_timer.Elapsed += TimerElapsed;
			_timer.Start();
		}

		public virtual event Action OnCancellation;

		public virtual bool IsCancellationRequested
		{
			get
			{
				if (_timerFired)
				{
					Cancel();
					return true;
				}

				return _cancellationTokenSource.IsCancellationRequested;
			}
		}

		private void TimerElapsed(object sender, ElapsedEventArgs args)
		{
			_timerFired = true;
			_timer?.Stop();
			Cancel();
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
			Dispose(true);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				_cancellationTokenSource.Dispose();
				_timer?.Dispose();
			}
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
			_timer?.Dispose();
			_timer = null;

			if (timeSpan < TimeSpan.Zero)
			{
				return;
			}

			InitTimer(timeSpan);
		}

		private sealed class CrossAppDomainCancellationToken : MarshalByRefObject, ICrossAppDomainCancellationToken
		{
			/// <summary>
			/// The token source. Can be null if this is a remote proxy instance (the class is sealed) or
			/// if this is a "None" token, one that can never be cancelled.
			/// </summary>
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

			/// <summary>
			/// Returns true if cancellation is requested. If source is null, this cannot be cancelled (or it's a proxy,
			/// in which case the local instance should never be called because the call goes trough the interface)
			/// </summary>
			public bool IsCancellationRequested => _source == null ? false : _source.IsCancellationRequested;

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

				if (_source == null)
				{
					return;
				}

				_source.OnCancellation += () =>
				{
					operation();
				};
			}
		}
	}
}
