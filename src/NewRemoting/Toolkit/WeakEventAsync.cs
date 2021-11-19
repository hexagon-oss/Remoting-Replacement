using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewRemoting.Collections;

namespace NewRemoting.Toolkit
{
	internal class WeakEventAsync<T> : WeakEvent<T>
		where T : class
	{
		private readonly TaskQueue _taskQueue;
		private AggregateException _lastAsyncException;

		internal WeakEventAsync(Action<AggregateException> onException, IWeakEventInvoker invoker)
		: base(onException, invoker)
		{
			_taskQueue = new TaskQueue();
			_lastAsyncException = null;
		}

		~WeakEventAsync()
		{
			if (_lastAsyncException != null)
			{
				throw new AggregateException("One or more exceptions inside an asynchronous event were not observed", _lastAsyncException.InnerExceptions);
			}
		}

		protected override bool InvokeTargets(object[] arguments, List<WeakEventEntry> eventEntries, out AggregateException aggregateException)
		{
			aggregateException = _lastAsyncException;
			_lastAsyncException = null;

			List<WeakEventEntry> localCopy = null;
			lock (_eventEntriesReadLock)
			{
				localCopy = new List<WeakEventEntry>(eventEntries);
			}

			_taskQueue.Add(() => RaiseTask(localCopy, arguments));
			return false; //// removal is handled directly by RaiseTask
		}

		private void RaiseTask(IEnumerable<WeakEventEntry> entries, object[] arguments)
		{
			var exceptions = new ConcurrentQueue<Exception>();
			bool removalRequired = false;
			Parallel.ForEach(entries, entry =>
			{
				try
				{
					removalRequired |= _invoker.InvokeTarget(entry, arguments);
				}
				catch (Exception e)
				{
					exceptions.Enqueue(e);
				}
			});
			if (removalRequired)
			{
				RemoveInvalidEntries();
			}

			if (!exceptions.IsEmpty)
			{
				var aggregateException = new AggregateException("Unhandled exception inside event handler", exceptions);
				if (!FireAggregateException(aggregateException))
				{
					_lastAsyncException = aggregateException;
				}
			}
		}
	}
}
