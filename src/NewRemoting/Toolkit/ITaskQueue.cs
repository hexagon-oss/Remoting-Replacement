using System;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	public interface ITaskQueue
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
		/// Adds a delegate with parameters for sequential execution to the task queue.
		/// Attention: Less performant than using an action.
		/// Returns false if queue is disabled.
		/// </summary>
		bool TryAdd(Delegate del, object[] para);

		/// <summary>
		/// Adds a task for sequential execution to the task queue.
		/// Returns false if queue is disabled.
		/// </summary>
		bool TryAdd(Task task);

		/// <summary>
		/// Tries to add an action for sequential execution to the task queue.
		/// Throws if queue is disabled.
		/// </summary>
		void Add(Action action);

		/// <summary>
		/// Adds a delegate with parameters for sequential execution to the task queue.
		/// Attention: Less performant than using an action.
		/// Throws if queue is disabled.
		/// </summary>
		void Add(Delegate del, object[] para);

		/// <summary>
		/// Adds a task for sequential execution to the task queue.
		/// Throws if queue is disabled.
		/// </summary>
		void Add(Task task);

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
