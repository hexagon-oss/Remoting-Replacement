using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	public interface ITaskQueue<T> : IEnumerable<T>
	{
		/// <summary>
		/// Returns the number of tasks in the queue
		/// </summary>
		int Count
		{
			get;
		}

		/// <summary>
		/// Tries to add an action for sequential execution to the task queue.
		/// Returns false if queue is disabled.
		/// </summary>
		bool TryAdd(Action action);

		/// <summary>
		/// Tries to add an action for sequential execution to the task queue.
		/// Throws if queue is disabled. The provided tag argument is readable
		/// from outside and provided as argument to the action once it is started.
		/// </summary>
		void Add(Action<T> action, T tag);

		/// <summary>
		/// Tries to add an action for sequential execution to the task queue.
		/// Returns false if queue is disabled.
		/// </summary>
		bool TryAdd(Action<T> action, T tag);

		/// <summary>
		/// Tries to add an action for sequential execution to the task queue.
		/// Throws if queue is disabled.
		/// </summary>
		void Add(Action action);

		/// <summary>
		/// Waits until the tasks queue becomes empty
		/// and all remaining tasks have finished
		/// returns false in case of timeout
		/// </summary>
		bool Flush(TimeSpan timeout);

		/// <summary>
		/// Waits until the tasks queue becomes empty and disabled the
		/// write queue. Afterwards all tasks added to the queue are discarded
		/// </summary>
		bool FlushAndDisable(TimeSpan timeout);
	}
}
