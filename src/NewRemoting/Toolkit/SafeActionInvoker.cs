using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// Invokes actions sync or async and exception safe.
	/// </summary>
	public static class SafeActionInvoker
	{
		#region Asynchronous
		public static void AsyncInvokeRemotingSafe(Action action, Action<Exception> onException)
		{
			InternalInvokeRemotingSafe<object, object, object, object, object>(action, onException, true);
		}

		public static void AsyncInvokeRemotingSafe<T1>(Action<T1> action, Action<Exception> onException, T1 para1)
		{
			InternalInvokeRemotingSafe<T1, object, object, object, object>(action, onException, true, new object[] { para1 });
		}

		public static void AsyncInvokeRemotingSafe<T1, T2>(Action<T1, T2> action, Action<Exception> onException, T1 para1, T2 para2)
		{
			InternalInvokeRemotingSafe<T1, T2, object, object, object>(action, onException, true, new object[] { para1, para2 });
		}

		public static void AsyncInvokeRemotingSafe<T1, T2, T3>(Action<T1, T2, T3> action, Action<Exception> onException, T1 para1, T2 para2, T3 para3)
		{
			InternalInvokeRemotingSafe<T1, T2, T3, object, object>(action, onException, true, new object[] { para1, para2, para3 });
		}

		public static void AsyncInvokeRemotingSafe<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, Action<Exception> onException, T1 para1, T2 para2, T3 para3, T4 para4)
		{
			InternalInvokeRemotingSafe<T1, T2, T3, T4, object>(action, onException, true, new object[] { para1, para2, para3, para4 });
		}

		public static void AsyncInvokeRemotingSafe<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, Action<Exception> onException, T1 para1, T2 para2, T3 para3, T4 para4, T5 para5)
		{
			InternalInvokeRemotingSafe<T1, T2, T3, T4, T5>(action, onException, true, new object[] { para1, para2, para3, para4, para5 });
		}
		#endregion

		#region Synchronous
		public static void InvokeRemotingSafe(Action action, Action<Exception> onException)
		{
			InternalInvokeRemotingSafe<object, object, object, object, object>(action, onException, false);
		}

		public static void InvokeRemotingSafe<T1>(Action<T1> action, Action<Exception> onException, T1 para1)
		{
			InternalInvokeRemotingSafe<T1, object, object, object, object>(action, onException, false, new object[] { para1 });
		}

		public static void InvokeRemotingSafe<T1, T2>(Action<T1, T2> action, Action<Exception> onException, T1 para1, T2 para2)
		{
			InternalInvokeRemotingSafe<T1, T2, object, object, object>(action, onException, false, new object[] { para1, para2 });
		}

		public static void InvokeRemotingSafe<T1, T2, T3>(Action<T1, T2, T3> action, Action<Exception> onException, T1 para1, T2 para2, T3 para3)
		{
			InternalInvokeRemotingSafe<T1, T2, T3, object, object>(action, onException, false, new object[] { para1, para2, para3 });
		}

		public static void InvokeRemotingSafe<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, Action<Exception> onException, T1 para1, T2 para2, T3 para3, T4 para4)
		{
			InternalInvokeRemotingSafe<T1, T2, T3, T4, object>(action, onException, false, new object[] { para1, para2, para3, para4 });
		}

		public static void InvokeRemotingSafe<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, Action<Exception> onException, T1 para1, T2 para2, T3 para3, T4 para4, T5 para5)
		{
			InternalInvokeRemotingSafe<T1, T2, T3, T4, T5>(action, onException, false, new object[] { para1, para2, para3, para4, para5 });
		}
		#endregion

		private static void InternalInvokeRemotingSafe<T1, T2, T3, T4, T5>(Delegate action, Action<Exception> onException, bool asynchronous, params object[] args)
		{
			SafeInvoke<T1, T2, T3, T4, T5>(action, onException, new List<Type> { typeof(RemotingException), typeof(SocketException) }, asynchronous, args);
		}

		/// <summary>
		/// Internal encapsulation of invoking with exception handling.
		/// Unhandled exceptions are thrown in the finalizer (when task gets collected).
		/// </summary>
		/// <exception cref="AggregateException">One or more eceptions caught during invoke</exception>
		private static void SafeInvoke<T1, T2, T3, T4, T5>(Delegate action, Action<Exception> onException, List<Type> exceptionsToCatch, bool asynchronous, params object[] args)
		{
			if (action != null)
			{
				Action invokeAction = () =>
				{
					List<Exception> exceptions = null;
					foreach (var singleFunction in action.GetInvocationList())
					{
						try
						{
							switch (args.Length)
							{
								case 0:
									((Action)singleFunction)();
									break;
								case 1:
									((Action<T1>)singleFunction)((T1)args[0]);
									break;
								case 2:
									((Action<T1, T2>)singleFunction)((T1)args[0], (T2)args[1]);
									break;
								case 3:
									((Action<T1, T2, T3>)singleFunction)((T1)args[0], (T2)args[1], (T3)args[2]);
									break;
								case 4:
									((Action<T1, T2, T3, T4>)singleFunction)((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3]);
									break;
								case 5:
									((Action<T1, T2, T3, T4, T5>)singleFunction)((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4]);
									break;
							}
						}
						catch (Exception e)
						{
							if (exceptions == null)
							{
								exceptions = new List<Exception>();
							}

							exceptions.Add(e);
						}
					}

					if (exceptions != null)
					{
						List<Exception> unhandledExceptions = null;
						foreach (var exception in exceptions)
						{
							if (exceptionsToCatch.Contains(exception.GetType()))
							{
								// Handle expected exception synchronous
								if (onException != null)
								{
									onException(exception);
								}
							}
							else
							{
								if (unhandledExceptions == null)
								{
									unhandledExceptions = new List<Exception>();
								}

								unhandledExceptions.Add(exception);
							}
						}

						if (unhandledExceptions != null)
						{
							throw new AggregateException(unhandledExceptions);
						}
					}
				};

				if (asynchronous)
				{
					// Execute async, unhanded exceptions are thrown in the finalizer (when GC collects)
					Task.Run(invokeAction);
				}
				else
				{
					invokeAction();
				}
			}
		}

	}
}
