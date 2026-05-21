using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// This class ensures, that all tasks passed to the queue are executed in sequential order.
	/// Every task can have an object assigned to it that can also be queried from outside (e.g. to identify
	/// what kind of tasks is in the queue)
	/// </summary>
	public class TaskQueue<T> : ITaskQueue<T>
	{
		private readonly Action<AggregateException> _exceptionHandler;
		private readonly object _queueLock = new object();
		private Queue<(Task Task, T Tag)> _taskQueue;
		private Task _currentTask;

		/// <summary>
		/// Constructor without custom exception handling.
		/// </summary>
		public TaskQueue()
		{
			_taskQueue = new Queue<(Task, T)>();
			_currentTask = null;
		}

		/// <summary>
		/// Constructor with custom exception handler.
		/// Must not implement dispose pattern due to this reference:
		/// Mostly the instance which holds this queue also implements the
		/// exception handler callback. If that instance is freed the instance
		/// of this queue is also freed.
		/// If another instance holds also a reference to this queue it would not make sense
		/// since the exception handler must also be alive otherwise the reference is
		/// to an invalid object.
		/// </summary>
		public TaskQueue(Action<AggregateException> exceptionHandler)
			: this()
		{
			if (exceptionHandler == null)
			{
				throw new ArgumentNullException(nameof(exceptionHandler));
			}

			_exceptionHandler = exceptionHandler;
		}

		public int Count
		{
			get
			{
				lock (_queueLock)
				{
					return _taskQueue != null ? _taskQueue.Count : 0;
				}
			}
		}

		public IEnumerator<T> GetEnumerator()
		{
			lock (_queueLock)
			{
				return _taskQueue.Select(x => x.Tag).GetEnumerator();
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public void Add(Action action)
		{
			AddInternal(action, true);
		}

		public bool TryAdd(Action action)
		{
			return AddInternal(action, false);
		}

		public void Add(Action<T> action, T tag)
		{
			AddInternal(action, tag, true);
		}

		public bool TryAdd(Action<T> action, T tag)
		{
			return AddInternal(action, tag, false);
		}

		private bool AddInternal(Action action, bool throwOnDisabled)
		{
			return AddInternal(x => action(), default, throwOnDisabled);
		}

		/// <summary>
		/// Adds a Tasks to the task Queue
		/// </summary>
		/// <exception cref="InvalidOperationException">Adding tasks to disabled queue is not allowed</exception>
		private bool AddInternal(Action<T> action, T tag, bool throwOnDisabled)
		{
			var added = false;
			lock (_queueLock)
			{
				if (_taskQueue == null && throwOnDisabled)
				{
					throw new InvalidOperationException("Adding tasks to disabled queue is not allowed");
				}

				if (_taskQueue != null)
				{
					_taskQueue.Enqueue((new Task(() => action(tag)), tag));
					added = true;
				}

				RunTask();
			}

			return added;
		}

		public bool Flush(TimeSpan timeout)
		{
			var stopwatch = new Stopwatch();
			stopwatch.Start();
			lock (_queueLock)
			{
				if (_taskQueue == null)
				{
					return true;
				}

				while (stopwatch.ElapsedMilliseconds < timeout.TotalMilliseconds)
				{
					if (_taskQueue.Count == 0)
					{
						return true;
					}

					Monitor.Wait(_queueLock, timeout);
				}
			}

			return false;
		}

		/// <summary>
		/// Disable the queue even if flush times out.
		/// </summary>
		public bool FlushAndDisable(TimeSpan timeout)
		{
			bool flushSuccess;
			lock (_queueLock)
			{
				flushSuccess = Flush(timeout);
				_taskQueue = null;
			}

			return flushSuccess;
		}

		private void RunTask()
		{
			lock (_queueLock)
			{
				if (_currentTask == null && _taskQueue != null && _taskQueue.Count > 0)
				{
					// Leave currently executing task in queue until fully executed, this makes waiting for all tasks executed much easier than when removing here and handling the currently executing task separately
					(_currentTask, _) = _taskQueue.Peek();
					var whereToContinueTask = _currentTask;
					if (_exceptionHandler != null)
					{
						whereToContinueTask = _currentTask.ContinueWith(t => _exceptionHandler(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
					}

					whereToContinueTask.ContinueWith(OnTaskFinished);
					_currentTask.Start();
				}
			}
		}

		private void OnTaskFinished(Task finishedTask)
		{
			lock (_queueLock)
			{
				_currentTask = null;
				if (_taskQueue != null)
				{
					_taskQueue.Dequeue();
				}

				Monitor.PulseAll(_queueLock);
				RunTask();
			}
		}
	}

	public class TaskQueue : TaskQueue<object>
	{
		public TaskQueue()
		{
		}

		public TaskQueue(Action<AggregateException> exceptionHandler)
			: base(exceptionHandler)
		{
		}
	}
}
