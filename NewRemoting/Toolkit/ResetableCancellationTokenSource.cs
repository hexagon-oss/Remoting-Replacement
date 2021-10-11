using System;
using System.Collections.Generic;
using System.Threading;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// Because <see cref="CancellationTokenSource"/> can not be reseted, this wrapper class is needed
	/// to replace the canceled <see cref="CancellationTokenSource"/> with a new one. This allows reusing the instance.
	/// </summary>
	public sealed class ResetableCancellationTokenSource : IDisposable
	{
		private readonly object _cancellationTokenLock;
		private readonly object _cancelAndResetLock;
		private readonly List<Action> _registeredCallbacks;
		private CancellationTokenSource _cancellationToken;

		public ResetableCancellationTokenSource()
		{
			_cancellationTokenLock = new object();
			_cancelAndResetLock = new object();

			_registeredCallbacks = new List<Action>();
			InitializeCancellationToken();
		}

		public bool IsCancellationRequested
		{
			get
			{
				lock (_cancellationTokenLock)
				{
					return _cancellationToken.IsCancellationRequested;
				}
			}
		}

		public WaitHandle WaitHandle
		{
			get
			{
				lock (_cancellationTokenLock)
				{
					return _cancellationToken.Token.WaitHandle;
				}
			}
		}

		public CancellationToken Token
		{
			get
			{
				lock (_cancellationTokenLock)
				{
					return _cancellationToken.Token;
				}
			}
		}

		private void InvokeSubscribers()
		{
			// Create local copy to avoid holding the lock during callback execution
			List<Action> callbacksLocal;
			lock (_cancellationTokenLock)
			{
				callbacksLocal = new List<Action>(_registeredCallbacks);
			}

			// Execute in sequence
			callbacksLocal.ForEach(callback => callback());
		}

		private void InitializeCancellationToken()
		{
			_cancellationToken = new CancellationTokenSource();
			_cancellationToken.Token.Register(InvokeSubscribers);
		}

		public void Reset()
		{
			lock (_cancelAndResetLock)
			{
				lock (_cancellationTokenLock)
				{
					_cancellationToken.Dispose();
					InitializeCancellationToken();
				}
			}
		}

		public void ThrowIfCancellationRequested()
		{
			lock (_cancellationTokenLock)
			{
				_cancellationToken.Token.ThrowIfCancellationRequested();
			}
		}

		public void Cancel()
		{
			lock (_cancelAndResetLock)
			{
				CancellationTokenSource localCopy = null;
				lock (_cancellationTokenLock)
				{
					localCopy = _cancellationToken;
				}

				localCopy.Cancel();
			}
		}

		public IDisposable Register(Action callback)
		{
			lock (_cancellationTokenLock)
			{
				_registeredCallbacks.Add(callback);
				return new CallbackRemover(this, callback);
			}
		}

		private void Unregister(Action callback)
		{
			lock (_cancellationTokenLock)
			{
				_registeredCallbacks.Remove(callback);
			}
		}

		public void Dispose()
		{
			_cancellationToken.Dispose();
			_registeredCallbacks.Clear();
		}

		private sealed class CallbackRemover : IDisposable
		{
			private readonly ResetableCancellationTokenSource _source;
			private readonly Action _callback;

			public CallbackRemover(ResetableCancellationTokenSource source, Action callback)
			{
				_source = source;
				_callback = callback;
			}

			public void Dispose()
			{
				_source.Unregister(_callback);
			}
		}
	}
}
