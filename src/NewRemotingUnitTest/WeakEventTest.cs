using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using NewRemoting;
using NewRemoting.Toolkit;

namespace NewRemotingUnitTest
{
	[TestFixture]
	public class WeakEventTest
	{
		public delegate void CustomDelegateType(string stringMessage);

		public static void StaticPrint(string message)
		{
			Console.WriteLine(message);
		}

		public void PrintSomething(string message, string message2)
		{
			Console.WriteLine(message);
			Console.WriteLine(message2);
		}

		public void PrintSomethingValueTypes(int message, int message2)
		{
			Console.WriteLine(message);
			Console.WriteLine(message2);
		}

		public void AddIntegers(int arg1, int arg2, int arg3)
		{
			Console.WriteLine(arg1 + arg2 + arg3);
		}

		[Test]
		public void CreateInvoker()
		{
			WeakEvent<Action> weakEvent = new WeakEvent<Action>(null, null);
			var methodInfo = GetType().GetMethod("PrintSomething");
			var invoker = weakEvent.CreateInvoker(methodInfo);
			invoker(this, new object[] { "foo", "boo" });

			methodInfo = GetType().GetMethod("PrintSomethingValueTypes");
			invoker = weakEvent.CreateInvoker(methodInfo);
			invoker(this, new object[] { 5, 6 });

			methodInfo = GetType().GetMethod("AddIntegers");
			invoker = weakEvent.CreateInvoker(methodInfo);
			invoker(this, new object[] { 5, 10, 100 });
		}

		[Test]
		public void CreateInvokerWrongParameterLength()
		{
			WeakEvent<Action> weakEvent = new WeakEvent<Action>(null, null);
			Exception ex = null;

			// wrong parameter length
			var methodInfo = GetType().GetMethod("AddIntegers");
			var invoker = weakEvent.CreateInvoker(methodInfo);
			invoker(this, new object[] { 5, 10, 100, 1000 });

			methodInfo = GetType().GetMethod("AddIntegers");
			invoker = weakEvent.CreateInvoker(methodInfo);
			try
			{
				invoker(this, new object[] { 5, 10 });
			}
			catch (IndexOutOfRangeException indOutOfRange)
			{
				ex = indOutOfRange;
			}

			Assert.That(ex, Is.Not.Null);
		}

		[Test]
		public void WrongTypes()
		{
			Exception ex = null;
			WeakEvent<Action> weakEvent = new WeakEvent<Action>(null, null);
			var methodInfo = GetType().GetMethod("AddIntegers");
			var invoker = weakEvent.CreateInvoker(methodInfo);
			try
			{
				invoker(this, new[] { "foo", new object() });
			}
			catch (InvalidCastException ce)
			{
				ex = ce;
			}

			Assert.That(ex, Is.Not.Null);
		}

		[Test]
		public void WithEnclosure()
		{
			var eventSource = new EventSource();
			string localValue = "Local_";
			for (int i = 0; i < 5; i++)
			{
				int localInt = i;
				eventSource.StringEvent += s =>
										   {
											   localValue = localValue + s + localInt;
										   };
			}

			GC.Collect();
			GC.WaitForPendingFinalizers();

			eventSource.FireStringEvent("fired");
			Assert.That(localValue, Is.EqualTo("Local_fired0fired1fired2fired3fired4"));
		}

		[Test]
		public void TriggerEvent()
		{
			var eventSource = new EventSource();
			var eventSink = new EventSink();
			eventSource.TwoIntegerEvent += eventSink.TwoIntegerSink;

			eventSource.StringEvent += eventSink.StringSink;
			eventSource.FireStringEvent("Trigger Event test");

			eventSource.FireTwoIntegerEvent(10, 5);
			Assert.That(eventSink._lastResult, Is.EqualTo(15));

			eventSource.FireTwoIntegerEvent(15, 11);
			Assert.That(eventSink._lastResult, Is.EqualTo(26));
		}

		[Test]
		public void AddAndRemoveEvents()
		{
			IWeakEvent<Action<int, int, int>> weakEvent = WeakEventBase.Create<Action<int, int, int>>();
			weakEvent.Add(AddIntegers);
			Assert.That(weakEvent.ClientCount, Is.EqualTo(1));
			weakEvent.Add(AddIntegers);
			Assert.That(weakEvent.ClientCount, Is.EqualTo(2));
			weakEvent.Remove(AddIntegers);
			Assert.That(weakEvent.ClientCount, Is.EqualTo(1));
			weakEvent.Remove(AddIntegers);
			Assert.That(weakEvent.ClientCount, Is.EqualTo(0));
		}

		[Test]
		public void TriggerDifferentArgumentTypes()
		{
			var eventSource = new EventSource();
			var eventSink = new EventSink();
			eventSource.MultiArgumentEvent += eventSink.MultiArgumentSink;

			eventSink._lastArguments.Clear();
			eventSource.FireMultiArgumentEvent(null, null, null, 0.0);
			Assert.That(eventSink._lastArguments, Is.EquivalentTo(new object[] { null, null, null, 0.0 }));

			eventSink._lastArguments.Clear();
			eventSource.FireMultiArgumentEvent(5, "foo", "boo", 50.0);
			Assert.That(eventSink._lastArguments, Is.EquivalentTo(new object[] { 5, "foo", "boo", 50.0 }));
		}

		[Test]
		public void AddAnonymousMethodWithoutEnclosure()
		{
			var eventSource = new EventSource();
			eventSource.StringEvent += s => Console.WriteLine(s);
			eventSource.FireStringEvent("foo");
		}

		[Test]
		public void AddAndRemoveStaticMethod()
		{
			IWeakEvent<Action<string>> weakEvent = WeakEventBase.Create<Action<string>>();
			weakEvent.Add(StaticPrint);
			Assert.That(weakEvent.ClientCount, Is.EqualTo(1));
			weakEvent.Raise("foo");
			weakEvent.Remove(StaticPrint);
			Assert.That(weakEvent.ClientCount, Is.EqualTo(0));
		}

		[Test]
		[Explicit("Currently disabled, because it fails")]
		public void TargetIsRemovedAfterCollected()
		{
			var eventSource = new EventSource();
			var eventSink = new EventSink();
			eventSource.TwoIntegerEvent += eventSink.TwoIntegerSink;

			eventSource.FireTwoIntegerEvent(10, 5);
			Assert.That(eventSink._lastResult, Is.EqualTo(15));
			Assert.That(eventSource.TwoIntEventClientCount, Is.EqualTo(1));

			eventSink = null;
			GC.Collect();
			GC.WaitForPendingFinalizers();

			eventSource.FireTwoIntegerEvent(15, 11);
			Assert.That(eventSource.TwoIntEventClientCount, Is.EqualTo(0));
		}

		[Test]
		public void AllDelegatesAreExecutedAndAggregateExceptionIsThrown()
		{
			var eventSource = new EventSource();
			var eventSink = new EventSink();

			eventSource.StringEvent += eventSink.ThrowsAnException;
			eventSource.StringEvent += eventSink.StringSink;
			eventSource.StringEvent += eventSink.ThrowsAnException;
			eventSource.StringEvent += eventSink.StringSink;
			eventSource.StringEvent += eventSink.ThrowsAnException;
			eventSource.StringEvent += eventSink.StringSink;
			eventSource.StringEvent += eventSink.ThrowsAnException;

			AggregateException aggregateException = null;
			try
			{
				eventSource.FireStringEvent("foo");
			}
			catch (AggregateException ae)
			{
				aggregateException = ae;
			}

			Assert.That(eventSink._lastArguments, Is.EquivalentTo(new object[] { "foo", "foo", "foo" }));
			Assert.That(aggregateException.InnerExceptions.Count, Is.EqualTo(4));
		}

		[Test]
		[Repeat(2)]
		public void MutliThreadingTest()
		{
			const int THREADCOUNT = 50;
			const int REPEATCOUNT = 100;
			Barrier start = new Barrier(THREADCOUNT + 1);
			Thread[] thrads = new Thread[THREADCOUNT];
			Exception exceptionInThread = null;
			var eventSource = new EventSource();

			var threadIds = new int[THREADCOUNT];

			for (int i = 0; i < THREADCOUNT; i++)
			{
				int threadIndex = i;
				// foreach thread a different event sink but same array
				var eventSink = new EventSink(threadIndex);
				eventSink._threadIds = threadIds;
				thrads[i] = new Thread(() =>
									   {
										   start.SignalAndWait();
										   try
										   {
											   for (int r = 0; r < REPEATCOUNT; r++)
											   {
												   eventSource.OneIntEvent += eventSink.IncrementThreadIdIndex;
												   eventSource.OneIntEvent -= eventSink.IncrementThreadIdIndex;
												   eventSource.FireOneIntEvent(threadIndex); // does nothing on the event sink
												   eventSource.OneIntEvent += eventSink.IncrementThreadIdIndex;
												   eventSource.FireOneIntEvent(threadIndex);
												   eventSource.OneIntEvent -= eventSink.IncrementThreadIdIndex;
											   }
										   }
										   catch (Exception ex)
										   {
											   exceptionInThread = ex;
										   }
									   });
				thrads[i].Start();
			}

			start.SignalAndWait();
			for (int i = 0; i < THREADCOUNT; i++)
			{
				thrads[i].Join();
			}

			if (exceptionInThread != null)
			{
				throw exceptionInThread;
			}

			for (int i = 0; i < THREADCOUNT; i++)
			{
				Assert.That(threadIds[i], Is.EqualTo(REPEATCOUNT));
			}
		}

		[Test]
		public void WeakEventRemoteAwareRemovesSubcscribersAfterRemoteExcetpion()
		{
			var eventSource = new EventSource();
			var eventSink = new EventSink();
			eventSource.StringEventRemoteAware += eventSink.StringSink;

			eventSource.FireStringEventRemoteAware("foo");
			Assert.That(eventSink._lastArguments[0], Is.EqualTo("foo"));
			Assert.That(eventSource.RemoteAwareClientCount, Is.EqualTo(1));

			eventSource.StringEventRemoteAware += eventSink.ThrowsRemotingException;
			Assert.That(eventSource.RemoteAwareClientCount, Is.EqualTo(2));

			eventSource.FireStringEventRemoteAware("foo");
			Assert.That(eventSource.RemoteAwareClientCount, Is.EqualTo(1));

			eventSource.StringEventRemoteAware -= eventSink.StringSink;
			Assert.That(eventSource.RemoteAwareClientCount, Is.EqualTo(0));
		}

		[Test]
		public void WeakEventRemoteAwareRemovesSubcscribersAfterRemoteExcetpionAsync()
		{
			int count = 0;

			IWeakEvent<Action<int>> oneIntAsyncRemoteAvare = WeakEventBase.Create<Action<int>>(null, true, true);
			oneIntAsyncRemoteAvare.Add(delegate(int i)
			{
				count += i;
				if (count == 2)
				{
					throw new RemotingException();
				}
			});
			oneIntAsyncRemoteAvare.Raise(1);
			oneIntAsyncRemoteAvare.Raise(1);
			Assert.That(() => oneIntAsyncRemoteAvare.ClientCount, Is.EqualTo(0).After(2000, 200));
			oneIntAsyncRemoteAvare.Raise(1);
			Assert.That(() => oneIntAsyncRemoteAvare.ClientCount, Is.EqualTo(0).After(2000, 200));
			Assert.That(count == 2);
		}

		[Test]
		public void AsyncExceptionsAreHandled()
		{
			int count = 0;
			AggregateException thrownException = null;
			Action<AggregateException> exceptionHandler = ex => thrownException = ex;
			IWeakEvent<Action<int>> oneIntAsyncRemoteAvare = WeakEventBase.Create<Action<int>>(exceptionHandler, true, true);
			oneIntAsyncRemoteAvare.Add(delegate(int i)
			{
				count += i;
				if (count == 2)
				{
					throw new Exception("Foo");
				}
			});
			oneIntAsyncRemoteAvare.Raise(1);
			oneIntAsyncRemoteAvare.Raise(1);
			Assert.That(() => thrownException, Is.Not.Null.After(2000, 100));
		}

		[Test]
		public void AsyncEventsDoesNotBlockRaiser()
		{
			var eventSource = new EventSource();
			var eventSink = new EventSink();
			eventSink._barrier = new Barrier(2);
			eventSource.OneIntEventAsync += eventSink.BlocksUntilSignal;
			eventSource.FireOneIntEventAsync(0);
			Assert.That(eventSink._lastArguments.Count, Is.EqualTo(0));
			eventSink._barrier.SignalAndWait(); // continue BlocksUntilSignal

			eventSink._barrier.SignalAndWait(); // wait until all tasks completed
			Assert.That(eventSink._lastArguments.Count, Is.EqualTo(1));
		}

		[Test]
		public void BlockingSubscriberOfAyncEventDoesNotBlockOthers()
		{
			AggregateException caughtException = null;
			Barrier nonBlockingHandlerExecuted = new Barrier(6);
			IWeakEvent<Action> weakEvent = WeakEventBase.Create<Action>(ex => caughtException = ex, true);

			weakEvent.Add(() => Assert.That(nonBlockingHandlerExecuted.SignalAndWait(20000)));
			weakEvent.Add(() => Assert.That(nonBlockingHandlerExecuted.SignalAndWait(20000)));
			weakEvent.Add(() => Assert.That(nonBlockingHandlerExecuted.SignalAndWait(20000)));
			weakEvent.Add(() => Assert.That(nonBlockingHandlerExecuted.SignalAndWait(20000)));
			weakEvent.Add(() => Assert.That(nonBlockingHandlerExecuted.SignalAndWait(20000)));

			weakEvent.Raise();

			Assert.That(nonBlockingHandlerExecuted.SignalAndWait(20000));
			if (caughtException != null)
			{
				throw caughtException;
			}
		}

		[Test]
		public void CreateWeakEventWithCustomDelegateType()
		{
			string localString = null;
			IWeakEvent<CustomDelegateType> weakEventWithCustomDelegateType = WeakEventBase.Create<CustomDelegateType>();
			weakEventWithCustomDelegateType.Add(str => localString = str);
			Assert.That(localString, Is.Null);
			weakEventWithCustomDelegateType.Raise("foo");
			Assert.That(localString, Is.EqualTo("foo"));
		}

		private sealed class EventSink
		{
			private readonly int _threadIndex;
			public readonly List<object> _lastArguments = new List<object>();
			public int _lastResult;
			public int[] _threadIds;
			public Barrier _barrier;

			public EventSink()
			{
			}

			public EventSink(int threadIndex)
			{
				_threadIndex = threadIndex;
			}

			public void TwoIntegerSink(int arg1, int arg2)
			{
				_lastResult = arg1 + arg2;
				Console.WriteLine("{0} + {1} = {2}", arg1, arg2, _lastResult);
				_lastArguments.AddRange(new object[] { arg1, arg2 });
			}

			public void MultiArgumentSink(int? arg1, object arg2, string arg3, double arg4)
			{
				_lastArguments.AddRange(new[] { arg1, arg2, arg3, arg4 });
			}

			public void StringSink(string message)
			{
				_lastArguments.Add(message);
				Console.WriteLine(message);
			}

			public void IncrementThreadIdIndex(int index)
			{
				if (index == _threadIndex)
				{
					_threadIds[index]++;
				}
			}

			public void BlocksUntilSignal(int index)
			{
				_barrier.SignalAndWait();
				_lastArguments.Add(index);
				_barrier.SignalAndWait();
			}

			// ReSharper disable once MemberCanBeMadeStatic.Local
			public void ThrowsAnException(string message)
			{
				throw new ApplicationException(message);
			}

			// ReSharper disable once MemberCanBeMadeStatic.Local
			public void ThrowsRemotingException(string message)
			{
				throw new RemotingException(message);
			}
		}

		private sealed class EventSource
		{
			private readonly IWeakEvent<Action<int>> _oneIntEvent = WeakEventBase.Create<Action<int>>();
			private readonly IWeakEvent<Action<int>> _oneIntAsync = WeakEventBase.Create<Action<int>>(null, true);
			private readonly IWeakEvent<Action<int, int>> _twoIntEvent = WeakEventBase.Create<Action<int, int>>();
			private readonly IWeakEvent<Action<string>> _stringEvent = WeakEventBase.Create<Action<string>>();
			private readonly IWeakEvent<Action<int?, object, string, double>> _multiArgumentEvent = WeakEventBase.Create<Action<int?, object, string, double>>();
			private readonly IWeakEvent<Action<string>> _stringEventRemoteAware = WeakEventBase.Create<Action<string>>(null, false, true);

			public event Action<int, int> TwoIntegerEvent
			{
				add
				{
					_twoIntEvent.Add(value);
				}

				remove
				{
					_twoIntEvent.Remove(value);
				}
			}

			public event Action<int> OneIntEvent
			{
				add
				{
					_oneIntEvent.Add(value);
				}

				remove
				{
					_oneIntEvent.Remove(value);
				}
			}

			public event Action<int> OneIntEventAsync
			{
				add
				{
					_oneIntAsync.Add(value);
				}

				remove
				{
					_oneIntAsync.Remove(value);
				}
			}

			public event Action<string> StringEvent
			{
				add
				{
					_stringEvent.Add(value);
				}

				remove
				{
					_stringEvent.Remove(value);
				}
			}

			public event Action<string> StringEventRemoteAware
			{
				add
				{
					_stringEventRemoteAware.Add(value);
				}

				remove
				{
					_stringEventRemoteAware.Remove(value);
				}
			}

			public event Action<int?, object, string, double> MultiArgumentEvent
			{
				add
				{
					_multiArgumentEvent.Add(value);
				}

				remove
				{
					_multiArgumentEvent.Remove(value);
				}
			}

			public int TwoIntEventClientCount
			{
				get
				{
					return _twoIntEvent.ClientCount;
				}
			}

			public int RemoteAwareClientCount
			{
				get
				{
					return _stringEventRemoteAware.ClientCount;
				}
			}

			public void FireTwoIntegerEvent(int arg1, int arg2)
			{
				_twoIntEvent.Raise(arg1, arg2);
			}

			public void FireStringEvent(string arg)
			{
				_stringEvent.Raise(arg);
			}

			public void FireStringEventRemoteAware(string arg)
			{
				_stringEventRemoteAware.Raise(arg);
			}

			public void FireMultiArgumentEvent(int? arg1, object arg2, string arg3, double arg4)
			{
				_multiArgumentEvent.Raise(arg1, arg2, arg3, arg4);
			}

			public void FireOneIntEvent(int arg)
			{
				_oneIntEvent.Raise(arg);
			}

			public void FireOneIntEventAsync(int arg)
			{
				_oneIntAsync.Raise(arg);
			}

		}
	}
}
