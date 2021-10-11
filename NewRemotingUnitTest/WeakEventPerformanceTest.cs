using System;
using System.Diagnostics;
using NUnit.Framework;
using NewRemoting;
using NewRemoting.Toolkit;

namespace NewRemotingUnitTest
{
	[TestFixture]
	// [Explicit("Event performance test")]
	internal class WeakEventPerformanceTest
	{
		private int _counter;

		public void DoesNothghing()
		{
			_counter++;
		}

		[Test]
		public void ComparePerformanceOneSubscriber()
		{
			_counter = 0;
			const int INVOCATIONCOUNT = 10000000;
			var eventSource = new EventSource();
			eventSource.WeakNoArgEvent += DoesNothghing;
			Stopwatch sw = Stopwatch.StartNew();
			for (int i = 0; i < INVOCATIONCOUNT; i++)
			{
				eventSource.FireWeakNoArgEvent();
			}

			Console.WriteLine("WeakEvent: {0:#,0} invocations took {1} ms", INVOCATIONCOUNT, sw.ElapsedMilliseconds);
			Assert.AreEqual(INVOCATIONCOUNT, _counter);

			_counter = 0;
			eventSource = new EventSource();
			eventSource.NormalNoArgEvent += DoesNothghing;
			sw = Stopwatch.StartNew();
			for (int i = 0; i < INVOCATIONCOUNT; i++)
			{
				eventSource.FireNormalNoArgEvent();
			}

			Console.WriteLine("Normal event: {0:#,0} invocations took {1} ms", INVOCATIONCOUNT, sw.ElapsedMilliseconds);
			Assert.AreEqual(INVOCATIONCOUNT, _counter);

			_counter = 0;
			eventSource = new EventSource();
			eventSource.NormalNoArgEvent += DoesNothghing;
			sw = Stopwatch.StartNew();
			for (int i = 0; i < INVOCATIONCOUNT; i++)
			{
				eventSource.FireNormalEventWithActionInvoker();
			}

			Console.WriteLine("SafeActionInvoker: {0:#,0} invocations took {1} ms", INVOCATIONCOUNT, sw.ElapsedMilliseconds);
			Assert.AreEqual(INVOCATIONCOUNT, _counter);

		}

		[Test]
		public void ComparePerformanceMultipleSubscribers()
		{
			const int SUBSCRIBERCOUNT = 1000000;
			var eventSource = new EventSource();
			Stopwatch sw = Stopwatch.StartNew();
			for (int i = 0; i < SUBSCRIBERCOUNT; i++)
			{
				eventSource.WeakNoArgEvent += DoesNothghing;
			}

			Console.WriteLine("WeakEvent: registering {0:#,0} subscribers took {1} ms", SUBSCRIBERCOUNT, sw.ElapsedMilliseconds);

			for (int i = 0; i < 5; i++)
			{
				_counter = 0;
				sw = Stopwatch.StartNew();
				eventSource.FireWeakNoArgEvent();
				Console.WriteLine("WeakEvent: 1 invocation with {0:#,0} subscribers took {1} ms", SUBSCRIBERCOUNT, sw.ElapsedMilliseconds);
				Assert.AreEqual(SUBSCRIBERCOUNT, _counter);
			}

			Console.WriteLine("---------------");

			eventSource = new EventSource();
			sw = Stopwatch.StartNew();
			for (int i = 0; i < SUBSCRIBERCOUNT; i++)
			{
				eventSource.NormalNoArgEvent += DoesNothghing;
			}

			Console.WriteLine("Normal event: registering {0:#,0} subscribers took {1} ms", SUBSCRIBERCOUNT, sw.ElapsedMilliseconds);
			for (int i = 0; i < 5; i++)
			{
				_counter = 0;
				sw = Stopwatch.StartNew();
				eventSource.FireNormalNoArgEvent();
				Console.WriteLine("Normal event: 1 invocation with {0:#,0} subscribers took {1} ms", SUBSCRIBERCOUNT, sw.ElapsedMilliseconds);
				Assert.AreEqual(SUBSCRIBERCOUNT, _counter);
			}

			Console.WriteLine("---------------");

			eventSource = new EventSource();
			sw = Stopwatch.StartNew();
			for (int i = 0; i < SUBSCRIBERCOUNT; i++)
			{
				eventSource.NormalNoArgEvent += DoesNothghing;
			}

			Console.WriteLine("SafeActionInvoker: registering {0:#,0} subscribers took {1} ms", SUBSCRIBERCOUNT, sw.ElapsedMilliseconds);
			for (int i = 0; i < 5; i++)
			{
				_counter = 0;
				sw = Stopwatch.StartNew();
				eventSource.FireNormalEventWithActionInvoker();
				Console.WriteLine("SafeActionInvoker: 1 invocation with {0:#,0} subscribers took {1} ms", SUBSCRIBERCOUNT, sw.ElapsedMilliseconds);
				Assert.AreEqual(SUBSCRIBERCOUNT, _counter);
			}

			Console.WriteLine("---------------");

		}

		private sealed class EventSource
		{
			private readonly IWeakEvent<Action> _noArgWeakEvent = WeakEventBase.Create<Action>();
			public event Action NormalNoArgEvent;

			public event Action WeakNoArgEvent
			{
				add
				{
					_noArgWeakEvent.Add(value);
				}

				remove
				{
					_noArgWeakEvent.Remove(value);
				}
			}

			public void FireWeakNoArgEvent()
			{
				_noArgWeakEvent.Raise();
			}

			public void FireNormalNoArgEvent()
			{
				NormalNoArgEvent();
			}

			public void FireNormalEventWithActionInvoker()
			{
				SafeActionInvoker.InvokeRemotingSafe(NormalNoArgEvent, null);
			}
		}
	}
}
