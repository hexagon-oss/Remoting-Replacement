using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NewRemoting;
using NewRemoting.Toolkit;
using NUnit.Framework;
using NUnit.Framework.Internal.Execution;
using SampleServerClasses;

namespace NewRemotingUnitTest
{
	[TestFixture(true)]
	[TestFixture(false)]
	[NonParallelizable]
	public class RemoteOperationsTest
	{
		private Process _serverProcess;
		private Client _client;
		private string _dataReceived;
		private string _dataReceived2;
		private AuthenticationHelper _helper;
		private int _currentProgress;
		private int _currentProgress2;
		private bool _callback0Triggered;

		public RemoteOperationsTest(bool withAuthentication)
		{
			if (withAuthentication)
			{
				_helper = new AuthenticationHelper();
			}
		}

		public static void TestStaticRoutine(int progress)
		{
		}

		public AuthenticationInformation CreateClientServer(bool interfaceOnly = false)
		{
			FileInfo fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
			var psi = new ProcessStartInfo(Path.Combine(fi.DirectoryName, "RemotingServer.exe"));

			Console.WriteLine($"Attempting to start {psi.FileName}...");
			Console.WriteLine($"Current directory is {Environment.CurrentDirectory}");
			AuthenticationInformation authenticationInfo = _helper == null ? null : new AuthenticationInformation(_helper.CertificateFileName, _helper.CertificatePassword);

			_serverProcess = Process.Start(psi);
			Assert.That(_serverProcess, Is.Not.Null);

			// Port is currently hardcoded
			_client = new Client("localhost", Client.DefaultNetworkPort, authenticationInfo, new ConnectionSettings()
			{
				InterfaceOnlyClient = interfaceOnly,
			});

			_client.Start();
			return authenticationInfo;
		}

		[SetUp]
		public void SetUp()
		{
			_helper?.SetUp();
		}

		[TearDown]
		public void TearDown()
		{
			_helper?.TearDown();

			GC.Collect();
			GC.WaitForPendingFinalizers();
			if (_client != null)
			{
				_client.ShutdownServer();
				_client.Dispose();
				_client = null;
			}

			if (_serverProcess != null)
			{
				Assert.That(_serverProcess.WaitForExit(5000));
				_serverProcess.Kill();
				_serverProcess.Dispose();
				_serverProcess = null;
			}
		}

		[Test]
		public void LocalObjectIsNotProxy()
		{
			var instance = new MarshallableClass();
			// There have been reports that this returns false positives
			Assert.That(!Client.IsRemoteProxy(instance));
		}

		[Test]
		public void GetInitialRemoteInstance()
		{
			CreateClientServer();
			var instance = CreateRemoteInstance();
			Assert.That(instance, Is.Not.Null);
			Assert.That(Client.IsRemoteProxy(instance));
		}

		[Test]
		public void RemoteInstanceCanBeCalled()
		{
			CreateClientServer();
			var instance = CreateRemoteInstance();
			int remotePid = instance.GetCurrentProcessId();
			Assert.That(Environment.ProcessId, Is.Not.EqualTo(remotePid));
		}

		[Test]
		public void TwoRemoteInstancesAreNotEqual()
		{
			CreateClientServer();
			var instance1 = CreateRemoteInstance();
			var instance2 = CreateRemoteInstance();
			Assert.That(instance2.Name, Is.Not.EqualTo(instance1.Name));
		}

		[Test]
		public void SameInstanceIsUsedInTwoCalls()
		{
			CreateClientServer();
			var instance1 = CreateRemoteInstance();
			string a = instance1.Name;
			string b = instance1.Name;
			Assert.That(b, Is.EqualTo(a));
		}

		[Test]
		public void CanCreateInstanceWithNonDefaultCtor()
		{
			CreateClientServer();
			var instance = _client.CreateRemoteInstance<MarshallableClass>("MyInstance");
			Assert.That(instance.Name, Is.EqualTo("MyInstance"));
		}

		[Test]
		public void CanMarshalSystemType()
		{
			CreateClientServer();
			var instance = CreateRemoteInstance();
			Assert.That(instance.GetTypeName(typeof(System.String)), Is.EqualTo("System.String"));
		}

		[Test]
		public void CanMarshalNullReference()
		{
			CreateClientServer();
			var instance = CreateRemoteInstance();
			instance.RegisterCallback(null);
		}

		[Test]
		public void CodeIsReallyExecutedRemotely()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			var client = new MarshallableClass();
			Assert.That(client.GetCurrentProcessId(), Is.Not.EqualTo(server.GetCurrentProcessId()));
		}

		[Test]
		public void RefArgumentWorks()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			int aValue = 4;
			server.UpdateArgument(ref aValue);
			Assert.That(aValue, Is.EqualTo(6));
		}

		[Test]
		public void CanRegisterCallbackInterface()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			// Tests whether the return channel works, by providing an instance of a class to the server where
			// the actual object lives on the client.
			var cbi = new CallbackImpl();
			Assert.That(!cbi.HasBeenCalled);
			server.RegisterCallback(cbi);
			server.DoCallback();
			server.RegisterCallback(null);
			Assert.That(cbi.HasBeenCalled);
		}

		[Test]
		public void DoesNotThrowExceptionIfAttemptingToRegisterPrivateMethodAsEventSink()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			var objectWithEvent = server.GetInterface<IMyComponentInterface>();
			_dataReceived = null;
			Assert.DoesNotThrow(() => objectWithEvent.TimeChanged += ObjectWithEventOnTimeChangedPrivate);
		}

		[Test]
		public void CanRegisterEvent()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			var objectWithEvent = server.GetInterface<IMyComponentInterface>();
			_dataReceived = null;
			objectWithEvent.TimeChanged += ObjectWithEventOnTimeChanged;
			objectWithEvent.StartTiming(TimeSpan.FromSeconds(1));
			int ticks = 100;
			while (string.IsNullOrEmpty(_dataReceived) && ticks-- > 0)
			{
				Thread.Sleep(100);
			}

			Assert.That(ticks > 0);
			Assert.That(!string.IsNullOrWhiteSpace(_dataReceived));
			objectWithEvent.StopTiming();
			objectWithEvent.TimeChanged -= ObjectWithEventOnTimeChanged;
		}

		[Test]
		[Repeat(5)]
		public void CanFireEventWhileDisconnecting()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			var objectWithEvent = server.GetInterface<IMyComponentInterface>();
			_dataReceived = null;
			objectWithEvent.TimeChanged += ObjectWithEventOnTimeChanged;
			objectWithEvent.StartTiming(TimeSpan.Zero);
			int ticks = 10000;
			while (ticks-- > 0)
			{
				objectWithEvent.TimeChanged -= ObjectWithEventOnTimeChanged;
				_dataReceived = null;
				objectWithEvent.TimeChanged += ObjectWithEventOnTimeChanged;
			}

			Thread.Sleep(100);
			Assert.That(!string.IsNullOrWhiteSpace(_dataReceived));
			objectWithEvent.StopTiming();
			objectWithEvent.TimeChanged -= ObjectWithEventOnTimeChanged;
		}

		private void SomeCallbackMethod(string arg1)
		{
			// nothing to do here
		}

		[Test]
		public void CanRegisterEventManyTimes()
		{
			CreateClientServer();
			IMarshallInterface server = CreateRemoteInstance();
			EventSink eventSink = new EventSink();

			Parallel.For(0, 100, x =>
			{
				server.AnEvent1 += eventSink.CallbackMethod;
				string msg = $"Iteration {x}";
				server.DoCallbackOnEvent(msg);
				server.AnEvent1 -= eventSink.CallbackMethod;
			});

			for (int i = 0; i < 100; i++)
			{
				server.AnEvent1 += eventSink.CallbackMethod;
				string msg = $"Iteration {i}";
				server.DoCallbackOnEvent(msg);
				server.AnEvent1 -= eventSink.CallbackMethod;
			}
		}

		[Test]
		public void CanRegisterRemoveEventLocally()
		{
			CreateClientServer();
			IMarshallInterface server = CreateRemoteInstance();
			var cb = new CallbackImpl();
			cb.InvokeCallback("Hi!");
			server.RegisterEventOnCallback(cb);
			cb.InvokeCallback("Test String");

			Assert.That(server.DeregisterEvent(), Is.EqualTo("Test String"));
			cb.InvokeCallback("Nothing happens now");
			Assert.That(server.DeregisterEvent(), Is.EqualTo("Test String"));
			server.RegisterEventOnCallback(null);
			Assert.That(server.DeregisterEvent(), Is.EqualTo("Test String"));

			GC.Collect();
			GC.WaitForPendingFinalizers();
			_client.ForceGc();
			cb.InvokeCallback("Maybe this crashes?"); // Yes, it does with V0.3.2
			cb = null;
			GC.Collect();
			GC.WaitForPendingFinalizers();
			_client.ForceGc();
			server.DeregisterEvent();
		}

		public void ObjectWithEventOnTimeChanged(DateTime arg1, string arg2)
		{
			_dataReceived = arg2;
		}

		private void ObjectWithEventOnTimeChangedPrivate(DateTime arg1, string arg2)
		{
			throw new NotSupportedException("This should never be called");
		}

		[Test]
		public void CreateRemoteInstanceWithNonDefaultCtor()
		{
			CreateClientServer();
			var arguments = new ConstructorArgument(new ReferencedComponent() { ComponentName = "ClientUnderTest" });
			var service = _client.CreateRemoteInstance<ServiceClass>(arguments);

			// This calls the server, who calls back into the client. So we get something that the client generated
			string roundTrippedAnswer = service.DoSomething();

			Assert.That(roundTrippedAnswer, Is.EqualTo("Wrapped by Server: ClientUnderTest"));
		}

		[Test]
		public void UseMixedInstanceAsArgument()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			var reference = new ReferencedComponent() { Data = 10 };
			// This is a serializable class that has a MarshalByRef member
			SerializableClassWithMarshallableMembers sc = new SerializableClassWithMarshallableMembers(1, reference);

			int reply = server.UseMixedArgument(sc);

			Assert.That(reply, Is.EqualTo(10));

			reply = sc.CallbackViaComponent();

			Assert.That(reply, Is.EqualTo(10));

			reference.Data = 20;
			reply = sc.CallbackViaComponent();
			Assert.That(reply, Is.EqualTo(20));

			var sc2 = sc.ReturnSelfToCaller();

			Assert.That(ReferenceEquals(sc, sc2));
		}

		/// <summary>
		/// This just verifies the test below
		/// </summary>
		[Test]
		public void UseSystemManagementLocally()
		{
			var bios = new CheckBiosVersion();

			string[] versions = bios.GetBiosVersions();
			Console.WriteLine($"Local bios versions are: {string.Join(", ", versions)}.");
		}

		[Test]
		public void ReflectionLoadSystemManagement()
		{
			// var name = new AssemblyName("System.Management");
			var name = new AssemblyName(
				"System.Management, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
			var assembly = Assembly.Load(name);
			Type t = assembly.GetType("System.Management.ManagementObjectSearcher", true);
			var instance = Activator.CreateInstance(t, "SELECT * FROM Win32_BIOS");

			Assert.That(instance, Is.Not.Null);
			((IDisposable)instance).Dispose();
		}

		[Test]
		public void UseRemoteSystemManagement()
		{
			CreateClientServer();
			var bios = _client.CreateRemoteInstance<CheckBiosVersion>();

			string[] versions = bios.GetBiosVersions();
			Console.WriteLine($"Server bios versions are: {string.Join(", ", versions)}.");
		}

		[Test]
		public void GetListOfMarshalByRefInstances()
		{
			CreateClientServer();
			var c = CreateRemoteInstance();
			var list = c.GetSomeComponents();
			Assert.That(list is List<ReferencedComponent>);
			Assert.That(list.Count, Is.EqualTo(2));
			Assert.That(list[0].ComponentName == list[1].ComponentName);
		}

		[Test]
		public void HandleRemoteException()
		{
			CreateClientServer();
			var c = CreateRemoteInstance();
			bool didThrow = true;
			try
			{
				c.MaybeThrowException(0);
				didThrow = false;
			}
			catch (DivideByZeroException x)
			{
				Assert.That(x, Is.Not.Null);
				Assert.That(x.StackTrace.Contains("DoIntegerDivide")); // The private method on the remote side that actually throws
				Console.WriteLine(x);
			}

			Assert.That(didThrow);
		}

		[Test]
		public void HandleRemoteExceptions()
		{
			CreateClientServer();
			var c = CreateRemoteInstance();
			bool didThrow = true;
			try
			{
				c.MaybeThrowException(1);
				didThrow = false;
			}
			catch (ArgumentOutOfRangeException x)
			{
				Assert.That(x, Is.Not.Null);
				Assert.That(x.InnerException, Is.Not.Null);
			}

			Assert.That(didThrow);
		}

		[Test]
		public void HandleRemoteAggregateExceptions()
		{
			CreateClientServer();
			var c = CreateRemoteInstance();

			try
			{
				c.ThrowAggregateException();
			}
			catch (AggregateException x)
			{
				Assert.That(x, Is.Not.Null);
				Assert.That(x.InnerException, Is.Not.Null);
				Assert.That(x.InnerExceptions.Count, Is.EqualTo(1));
				Assert.That(x.InnerExceptions[0] is AggregateException);
				var aggregates = x.InnerExceptions[0] as AggregateException;
				Assert.That(aggregates.InnerExceptions.Count, Is.EqualTo(2));
			}
		}

		[Test]
		public void GetRemotingServerService()
		{
			CreateClientServer();
			var serverService = _client.RequestRemoteInstance<IRemoteServerService>();
			Assert.That(serverService.Ping());
		}

		[Test]
		public void SerializeUnserializableArgument()
		{
			CreateClientServer();
			var cls = CreateRemoteInstance();
			Assert.Throws<SerializationException>(() => cls.CallerError(new UnserializableObject()));
		}

		[Test]
		public void SerializeDtoAsInterface()
		{
			CreateClientServer();
			var cls = CreateRemoteInstance();
			IMyDto returned = cls.GetDto("Test");
			Assert.That(returned, Is.Not.Null);
			Assert.That(returned, Is.InstanceOf<SerializableType>());
			Assert.That(returned.Name, Is.EqualTo("Test"));
			Assert.That(returned.Id, Is.EqualTo(4));
		}

		[Test]
		public void SerializeUnserializableReturnType()
		{
			CreateClientServer();
			var cls = CreateRemoteInstance();
			Assert.Throws<SerializationException>(() => cls.ServerError());
		}

		[Test]
		public void TwoClientsCanConnect()
		{
			AuthenticationInformation authenticationInfo = CreateClientServer();
			var client2 = new Client("localhost", Client.DefaultNetworkPort, authenticationInfo, new ConnectionSettings());
			client2.Start();

			var firstInstance = _client.CreateRemoteInstance<MarshallableClass>();
			var secondInstance = client2.CreateRemoteInstance<MarshallableClass>();
			Assert.That(secondInstance, Is.Not.EqualTo(firstInstance));

			// Test that the callback ends on the correct client.
			var cb1 = new CallbackImpl();
			var cb2 = new CallbackImpl();
			firstInstance.RegisterCallback(cb1);
			secondInstance.RegisterCallback(cb2);
			firstInstance.DoCallback();
			Assert.That(cb1.HasBeenCalled);
			Assert.That(!cb2.HasBeenCalled);

			client2.Disconnect();
			client2.Dispose();

			// This is still operational
			Assert.That(firstInstance.GetSomeData() != 0);
		}

		[Test]
		public void CanRegisterUnregisterEvents()
		{
			CreateClientServer();
			IMarshallInterface instance = _client.CreateRemoteInstance<MarshallableClass>();

			_dataReceived = null;
			instance.DoCallbackOnEvent("Test string");

			Assert.That(_dataReceived, Is.Null);
			instance.AnEvent2 += CallbackMethod;
			instance.DoCallbackOnEvent("Another test string");
			Assert.That(_dataReceived, Is.EqualTo("Another test string"));
			_dataReceived = null;
			instance.AnEvent2 -= CallbackMethod;
			instance.DoCallbackOnEvent("A final test string");
			Assert.That(_dataReceived, Is.Null);

			_callback0Triggered = false;
			instance.AnEvent0 += CallbackMethod0;
			instance.DoCallbackOnEvent(string.Empty);
			Assert.That(_callback0Triggered);
			_callback0Triggered = false;
			instance.AnEvent0 -= CallbackMethod0;
			instance.DoCallbackOnEvent(string.Empty);
			Assert.That(_callback0Triggered, Is.False);

			Assert.Throws<InvalidRemotingOperationException>(() => instance.AnEvent5 += CallbackMethod5);
		}

		public void ProgressFeedback(int progress)
		{
			_currentProgress = progress;
		}

		public void ProgressFeedback2(int progress)
		{
			_currentProgress2 = progress;
		}

		[Test]
		public void ForwardCallback()
		{
			CreateClientServer();
			IMarshallInterface instance = _client.CreateRemoteInstance<MarshallableClass>();
			_currentProgress = 0;
			_currentProgress2 = 0;
			instance.RegisterEvent(ProgressFeedback);
			instance.SetProgress(5);
			Assert.That(_currentProgress, Is.EqualTo(5));

			// Test that replacing the progress feedback works
			instance.RegisterEvent(ProgressFeedback2);
			instance.SetProgress(50);
			Assert.That(_currentProgress, Is.EqualTo(5));
			Assert.That(_currentProgress2, Is.EqualTo(50));
		}

		[Test]
		public void ForwardStaticCallbackThrows()
		{
			CreateClientServer();
			IMarshallInterface instance = _client.CreateRemoteInstance<MarshallableClass>();
			Assert.Throws<InvalidRemotingOperationException>(() => instance.RegisterEvent(TestStaticRoutine));
		}

		private void CallbackMethod4(string message, string caller)
		{
			_dataReceived = message + "from" + caller;
		}

		private void CallbackMethod2a(string message, string caller)
		{
			_dataReceived2 = message + "from" + caller;
		}

		[Test]
		public void CanRegisterUnregisterEventWithoutAffectingOtherInstance([Values]bool remote)
		{
			CreateClientServer();
			IMarshallInterface instance = CreateInstance(remote, "instance0");
			IMarshallInterface instance2 = CreateInstance(remote, "instance2");
			_dataReceived = null;
			instance.DoCallbackOnEvent("Initial test string");

			Assert.That(_dataReceived, Is.Null);
			instance.AnEvent2 += CallbackMethod4;
			instance2.AnEvent2 += CallbackMethod4;
			_dataReceived = null;
			instance.AnEvent2 -= CallbackMethod4;
			instance.DoCallbackOnEvent("More testing");
			Assert.That(_dataReceived, Is.Null);
			instance2.DoCallbackOnEvent("And yet another test");
			Assert.That(_dataReceived, Is.Not.Null);
		}

		[Test]
		public void CanRegisterTwoCallbacks([Values] bool remote)
		{
			CreateClientServer();
			IMarshallInterface instance = CreateInstance(remote, "instance0");
			_dataReceived = null;
			instance.DoCallbackOnEvent("Initial test string");

			Assert.That(_dataReceived, Is.Null);
			instance.AnEvent2 += CallbackMethod4;
			instance.AnEvent2 += CallbackMethod2a;
			_dataReceived = null;
			_dataReceived2 = null;
			instance.DoCallbackOnEvent("Some test string");
			Assert.That(_dataReceived, Is.Not.Null);
			Assert.That(_dataReceived2, Is.Not.Null);
			_dataReceived = null;
			_dataReceived2 = null;
			instance.AnEvent2 -= CallbackMethod4;
			instance.DoCallbackOnEvent("Simple test string");
			Assert.That(_dataReceived, Is.Null);
			Assert.That(_dataReceived2, Is.Not.Null);
			instance.AnEvent2 -= CallbackMethod2a;
		}

		[Test]
		public void RegisterForCallbackReverse([Values] bool remote)
		{
			CreateClientServer();
			IMarshallInterface instance = CreateInstance(remote, "instance0");
			_dataReceived = null;

			ICallbackInterface localStuff = new CallbackImpl();
			instance.RegisterForCallback(localStuff);
			localStuff.InvokeCallback("Test");
			instance.EnsureCallbackWasUsed();
		}

		[Test]
		public void CanRegisterCallbackOnDifferentInstance([Values] bool remote)
		{
			CreateClientServer();
			IMarshallInterface instance = CreateInstance(remote, "instance0");
			instance.DoCallbackOnEvent("Initial test string");

			EventSink sink = new EventSink();

			instance.AnEvent2 += sink.RegisterThis;

			instance.DoCallbackOnEvent("Test");
			Assert.That(sink.Data, Is.EqualTo("Test from instance0"));

			sink.Data = null;
			instance.AnEvent2 -= sink.RegisterThis;
			instance.DoCallbackOnEvent("No test");
			Assert.That(sink.Data, Is.Null);
		}

		[Test]
		public void CanRegisterCallbackOnDifferentInstance2([Values] bool remote)
		{
			CreateClientServer();
			IMarshallInterface instance = CreateInstance(remote, "instance0");
			instance.DoCallbackOnEvent("Initial test string");

			EventSink sink = new EventSink();
			EventSink anotherSink = new EventSink();

			instance.AnEvent2 += sink.RegisterThis;
			instance.AnEvent2 += anotherSink.RegisterThis;

			instance.DoCallbackOnEvent("Test");
			Assert.That(sink.Data, Is.EqualTo("Test from instance0"));
			Assert.That(anotherSink.Data, Is.EqualTo("Test from instance0"));

			sink.Data = null;
			instance.AnEvent2 -= sink.RegisterThis;
			instance.DoCallbackOnEvent("Test2");
			Assert.That(sink.Data, Is.Null);
			Assert.That(anotherSink.Data, Is.EqualTo("Test2 from instance0"));
		}

		[Test]
		public void RemovingAnAlreadyRemovedDelegateDoesNothing()
		{
			CreateClientServer();
			EventHandling();
			IMarshallInterface instance = _client.CreateRemoteInstance<MarshallableClass>(nameof(RemovingAnAlreadyRemovedDelegateDoesNothing));

			_dataReceived = null;
			instance.DoCallbackOnEvent("Test string");

			Assert.That(_dataReceived, Is.Null);
			instance.AnEvent2 += CallbackMethod;
			instance.DoCallbackOnEvent("Another test string");
			Assert.That(_dataReceived, Is.EqualTo("Another test string"));
			_dataReceived = null;
			instance.AnEvent2 -= CallbackMethod;
			instance.DoCallbackOnEvent("A third test string");
			Assert.That(_dataReceived, Is.Null);
			instance.AnEvent2 -= CallbackMethod;
			instance.DoCallbackOnEvent("Test string 4");
			Assert.That(_dataReceived, Is.Null);
		}

		/// <summary>
		/// Does the same as above, but with a local target object
		/// </summary>
		[Test]
		public void RemovingAnAlreadyRemovedDelegateDoesNothingLocal()
		{
			IMarshallInterface instance = new MarshallableClass("Test");

			_dataReceived = null;
			instance.DoCallbackOnEvent("Test string");
			Assert.That(_dataReceived, Is.Null);
			instance.AnEvent2 += CallbackMethod;
			instance.DoCallbackOnEvent("Another test string");
			Assert.That(_dataReceived, Is.EqualTo("Another test string"));
			_dataReceived = null;
			instance.AnEvent2 -= CallbackMethod;
			instance.DoCallbackOnEvent("A third test string");
			Assert.That(_dataReceived, Is.Null);
			instance.AnEvent2 -= CallbackMethod;
			instance.DoCallbackOnEvent("Test string 4");
			Assert.That(_dataReceived, Is.Null);
		}

		[Test]
		public void CopyStreamToServer()
		{
			CreateClientServer();
			MemoryStream ms = new MemoryStream();
			byte[] data = new byte[10 * 1024 * 1024];
			data[1] = 2;
			ms.Write(data);
			var server = CreateRemoteInstance();
			Stopwatch sw = Stopwatch.StartNew();
			Assert.That(server.StreamDataContains(ms, 2));
			Debug.WriteLine($"Stream of size {ms.Length} took {sw.Elapsed} to send");
		}

		[Test]
		public void GetMemoryStreamFromServer()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			Stream s = server.GetPooledStream();
			Assert.That(s.Length == 202);
			byte b = (byte)s.ReadByte();
			Assert.That(b, Is.EqualTo(0xfe));
			Assert.DoesNotThrow(() => server.CreateCalc()); // Just to make sure the stream is still in sync
		}

		[Test]
		public void FileStreamFromClientToServer()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			string fileToOpen = Assembly.GetExecutingAssembly().Location;
			using FileStream fs = new FileStream(fileToOpen, FileMode.Open, FileAccess.Read);
			Assert.That(server.CheckStreamEqualToFile(fileToOpen, fs.Length, fs));
		}

		[Test]
		public void CreateFileStreamOnRemoteServer()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			string fileToOpen = Assembly.GetExecutingAssembly().Location;
			using var stream = server.GetFileStream(fileToOpen);
			Assert.That(stream, Is.Not.Null);
			Assert.That(stream is FileStream);
			FileStream fs = (FileStream)stream;
			Assert.That(fs.Length > 0);
			Assert.That(fs.Name, Is.Not.Null);
			Assert.That(fs.ReadByte(), Is.EqualTo('M')); // This is a dll file. It starts with the letters "MZ".
			server.CloseStream(stream);
			fs.Dispose();
		}

		[Test]
		public void CanUseAnArgumentThatIsGeneric()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			var list = new List<int>();
			list.Add(1);
			list.Add(2);
			Assert.That(server.ListCount(list), Is.EqualTo(2));
		}

		[Test]
		public void DistributedGcTest1()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			var component = server.GetComponent();
			Assert.That(component, Is.Not.Null);
			component = null;
			GC.Collect();
			GC.WaitForPendingFinalizers();
			_client.ForceGc();
			// This may just recreate a new remote reference, so this test is not very expressive.
			// But we can't keep a reference to the object to check whether it is properly GC'd.
			Assert.That(server.GetComponent(), Is.Not.Null);
		}

		[Test]
		public void VerifyMatchingServer()
		{
			CreateClientServer();
			Assert.That(_client.VerifyMatchingServer(), Is.Null);
		}

		public void EventHandling()
		{
			int expectedCounter = 0;

			var instance = _client.CreateRemoteInstance<MarshallableClass>();
			instance.DoCallbackOnEvent("Utest");

			ExecuteCallbacks(instance, 4, 10,  ref expectedCounter);
		}

		[Test]
		public void EventHandlingTest()
		{
			CreateClientServer();
			EventHandling();
		}

		[Test]
		public void ParallelGetObjectInstance()
		{
			CreateClientServer();
			var server = _client.CreateRemoteInstance<MarshallableClass>();
			server.CreateCalc();
			SimpleCalc localCalc = new SimpleCalc();
			Parallel.For(0, 100, x =>
			{
				if (x != 0) // Not on the primary thread
				{
					var calc = server.DetermineCalc();
					Assert.That(calc, Is.Not.Null);
					double result = calc.Add(x, x);
					Assert.That(result, Is.EqualTo(2 * x).Within(1E-10));

					_client.ForceGc();
					calc.DooFoo(localCalc);
				}
			});
		}

		[Test]
		public void TestSealedProxy()
		{
			CreateClientServer();
			var server = _client.CreateRemoteInstance<MarshallableClass>();
			object instance = server.GetSealedClass();

			IMyComponentInterface interf = (IMyComponentInterface)instance;
			var data = interf.ProcessName();
			IDisposable disp = (IDisposable)interf;
			disp.Dispose();

			Assert.That(string.IsNullOrWhiteSpace(data), Is.False);
		}

		[Test]
		public void TestFastArgumentPassing()
		{
			CreateClientServer();
			var server = _client.CreateRemoteInstance<MarshallableClass>();
			Assert.That(server.TakeSomeArguments(10, 2000, 2000, 10.0), Is.True);
			Assert.That(server.TakeSomeMoreArguments(10, 2000, 2000, 10.0f), Is.True);
		}

		[Test]
		public void TestTypeAsArguments()
		{
			CreateClientServer();
			var server = _client.CreateRemoteInstance<MarshallableClass>();
			var result = server.ProcessListOfTypes();
			Assert.That(result, Is.Empty);
			result = server.ProcessListOfTypes(typeof(string), typeof(Int32), typeof(bool), null);
			Assert.That(string.IsNullOrWhiteSpace(result), Is.False);
		}

		[Test]
		public void UseStructAsArgument()
		{
			CreateClientServer();
			var server = _client.CreateRemoteInstance<MarshallableClass>();
			var sent = new CustomSerializableObject();
			sent.Time = DateTime.Now;
			sent.Value = 100;
			var result = server.SomeStructOperation(sent);
			Assert.That(result.Value, Is.EqualTo(10));

			result = server.SomeStructOperation(new CustomSerializableObject()); // Repeat
		}

		[Test]
		public void UseStructAsReturnType()
		{
			CreateClientServer();
			var server = _client.CreateRemoteInstance<MarshallableClass>();
			var result = server.GetStruct();
			Assert.That(result.Data1, Is.EqualTo(10));
			Assert.That(result.Data2, Is.EqualTo(24.22).Within(1E-10));
		}

		[Test]
		public void CanReadAndWriteLargeRemoteFile()
		{
			const int blocks = 10;
			const int blockSize = 1024 * 1024 * 512;
			CreateClientServer();
			var server = CreateRemoteInstance();
			var stream = server.OpenStream("test.dat", FileMode.Create, FileAccess.ReadWrite);

			byte[] buffer = new byte[blockSize];
			byte[] buffer2 = new byte[buffer.Length];

			for (int i = 0; i < buffer.Length; i++)
			{
				buffer[i] = (byte)i;
			}

			for (int j = 0; j < blocks; j++)
			{
				stream.Write(buffer, 0, buffer.Length);
			}

			stream.Position = 0;

			for (int k = 0; k < blocks; k++)
			{
				int dataRead = stream.Read(buffer2, 0, buffer.Length);
				Assert.That(dataRead, Is.EqualTo(buffer.Length));
				Assert.That(buffer2[k], Is.EqualTo(buffer[k]));
			}

			stream.Close();
			server.DeleteFile("test.dat");
		}

		[Test]
		public void CanReadManuallySerializedObjectDirectly()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			var obj = server.GetSerializedObject();
			Assert.That(obj, Is.Not.Null);
			Assert.That(obj.Length, Is.EqualTo(10));
			Assert.That(obj[1], Is.EqualTo(5));
		}

		[Test]
		public void CanReadManuallySerializedObjectIndirectly()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			var obj = server.GetSerializedObjects();
			Assert.That(obj.A, Is.Not.Null);
			Assert.That(obj.A.Length, Is.EqualTo(5));
			Assert.That(obj.A[0], Is.EqualTo(6));
			Assert.That(obj.B[0], Is.Not.EqualTo(6));
		}

		[Test]
		public void CanGetListAsOutParameter()
		{
			CreateClientServer();
			var server = CreateRemoteInstance();
			Assert.That(server.GetMyImportantList(out var data));
			Assert.That(data, Is.Not.Empty);
			Assert.That(data[0].Value, Is.EqualTo(1));
			Assert.That(data[1].Value, Is.EqualTo(2));
			Assert.That(data[1].AnotherValue, Is.Not.EqualTo(0));
		}

		[Test]
		public void StartInterfaceOnlyClient()
		{
			CreateClientServer(true);
			var server = CreateRemoteInstance();
			var interf = server.GetInterface<IDisposable>();
			Assert.That(interf, Is.Not.Null);
		}

		[Test]
		public void RemoteWaitAnyAndWaitAll()
		{
			CreateClientServer(true);
			var server = CreateRemoteInstance();
			var handles = server.QuerySomeHandles();
			Assert.That(RemoteWaitHandle.WaitAll(handles.ToArray(), TimeSpan.Zero) == false);
			Assert.That(RemoteWaitHandle.WaitAny(handles.ToArray(), TimeSpan.MaxValue) != WaitHandle.WaitTimeout);
			Assert.That(RemoteWaitHandle.WaitAll(handles.ToArray(), TimeSpan.MaxValue));
		}

		private void ExecuteCallbacks(IMarshallInterface instance, int overallIterations, int iterations,
			ref int expectedCounter)
		{
			_dataReceived = null;
			for (int j = 0; j < overallIterations; j++)
			{
				instance.AnEvent2 += CallbackMethod;
				for (int i = 0; i < iterations; i++)
				{
					int cnt = ++expectedCounter;
					instance.DoCallbackOnEvent("Utest" + cnt);
					Assert.That(_dataReceived, Is.EqualTo("Utest" + cnt));
				}

				if (j % 2 == 0)
				{
					_client.ForceGc();
				}

				instance.AnEvent2 -= CallbackMethod;
			}
		}

		private MarshallableClass CreateRemoteInstance()
		{
			return _client.CreateRemoteInstance<MarshallableClass>();
		}

		private IMarshallInterface CreateInstance(bool remote, string name)
		{
			if (remote)
			{
				return _client.CreateRemoteInstance<MarshallableClass>(name);
			}
			else
			{
				return new MarshallableClass(name);
			}
		}

		public void CallbackMethod(string argument, string senderInstance)
		{
			if (!senderInstance.StartsWith("Unnamed"))
			{
				Console.WriteLine($"CallbackMethod: Previous value {_dataReceived}, new value {argument}, sender {senderInstance}");
			}

			_dataReceived = argument;
		}

		public void CallbackMethod0()
		{
			_callback0Triggered = true;
		}

		public void CallbackMethod5(string argument1, string argument2, string argument3, string argument4, string argument5)
		{
		}

		private sealed class CallbackImpl : MarshalByRefObject, ICallbackInterface
		{
			public CallbackImpl()
			{
				HasBeenCalled = false;
			}

			public event Action<string> Callback;

			public bool HasBeenCalled { get; set; }

			public void FireSomeAction(string nameOfAction)
			{
				HasBeenCalled = true;
			}

			public void InvokeCallback(string data)
			{
				Callback?.Invoke(data);
			}
		}

		private sealed class EventSink
		{
			public string Data
			{
				get;
				set;
			}

			public void RegisterThis(string msg, string source)
			{
				Data = $"{msg} from {source}";
			}

			public void CallbackMethod(string obj)
			{
				Data = obj;
			}
		}
	}
}
