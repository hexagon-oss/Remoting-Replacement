using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// This class ensures, that all tasks passed to the queue are executed in sequential order
	/// </summary>
	public sealed class TaskQueue : ITaskQueue
	{
		private readonly Action<AggregateException> _exceptionHandler;
		private readonly object _queueLock = new object();
		private Queue<Task> _taskQueue;
		private Task _currentTask;

		/// <summary>
		/// Constructor without custom exception handling.
		/// </summary>
		public TaskQueue()
		{
			_taskQueue = new Queue<Task>();
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

		public void Add(Action action)
		{
			AddInternal(action, true);
		}

		public void Add(Delegate del, object[] para)
		{
			AddInternal(del, para, true);
		}

		public void Add(Task task)
		{
			AddInternal(task, true);
		}

		public bool TryAdd(Action action)
		{
			return AddInternal(action, false);
		}

		/// <summary>
		/// Attention: is very slow consider wrapping time critical stuff into an action <see cref="Add(Action)" />.
		/// <code>
		/// Add(new Action(() => Call(p1, p2, p3, ...));
		/// </code>
		/// </summary>
		public bool TryAdd(Delegate del, object[] para)
		{
			return AddInternal(del, para, false);
		}

		public bool TryAdd(Task task)
		{
			return AddInternal(task, false);
		}

		private bool AddInternal(Action action, bool throwOnDisabled)
		{
			return AddInternal(new Task(action), throwOnDisabled);
		}

		/// <summary>
		/// <see cref="Delegate.DynamicInvoke"/> is very slow
		/// </summary>
		private bool AddInternal(Delegate del, object[] para, bool throwOnDisabled)
		{
			return AddInternal(new Task(() => del.DynamicInvoke(para)), throwOnDisabled);
		}

		/// <summary>
		/// Adds a Tasks to the task Queue
		/// </summary>
		/// <exception cref="InvalidOperationException">Adding tasks to disabled queue is not allowed</exception>
		private bool AddInternal(Task task, bool throwOnDisabled)
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
					_taskQueue.Enqueue(task);
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
					_currentTask = _taskQueue.Peek();
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
}
