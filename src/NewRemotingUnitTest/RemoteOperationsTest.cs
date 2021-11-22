using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using NewRemoting;
using NUnit.Framework;
using SampleServerClasses;

namespace NewRemotingUnitTest
{
	[TestFixture]
	public class RemoteOperationsTest
	{
		private Process _serverProcess;
		private Client _client;
		private string _dataReceived;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			_serverProcess = Process.Start("RemotingServer.exe");
			Assert.IsNotNull(_serverProcess);

			// Port is currently hardcoded
			_client = new Client("localhost", Client.DefaultNetworkPort);
			_client.Start();
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			if (_client != null)
			{
				_client.ShutdownServer();
				_client.Dispose();
				_client = null;
			}

			if (_serverProcess != null)
			{
				Assert.That(_serverProcess.WaitForExit(2000));
				_serverProcess.Kill();
				_serverProcess = null;
			}
		}

		[Test]
		public void LocalObjectIsNotProxy()
		{
			var instance = new MarshallableClass();
			// There have been reports that this returns false positives
			Assert.False(Client.IsRemoteProxy(instance));
		}

		[Test]
		public void GetInitialRemoteInstance()
		{
			var instance = CreateRemoteInstance();
			Assert.IsNotNull(instance);
			Assert.That(Client.IsRemoteProxy(instance));
		}

		[Test]
		public void RemoteInstanceCanBeCalled()
		{
			var instance = CreateRemoteInstance();
			int remotePid = instance.GetCurrentProcessId();
			Assert.AreNotEqual(remotePid, Environment.ProcessId);
		}

		[Test]
		public void TwoRemoteInstancesAreNotEqual()
		{
			var instance1 = CreateRemoteInstance();
			var instance2 = CreateRemoteInstance();
			Assert.AreNotEqual(instance1.Identifier, instance2.Identifier);
		}

		[Test]
		public void SameInstanceIsUsedInTwoCalls()
		{
			var instance1 = CreateRemoteInstance();
			long a = instance1.Identifier;
			long b = instance1.Identifier;
			Assert.AreEqual(a, b);
		}

		[Test]
		public void CanCreateInstanceWithNonDefaultCtor()
		{
			var instance = _client.CreateRemoteInstance<MarshallableClass>(23);
			Assert.AreEqual(23, instance.Identifier);
		}

		[Test]
		public void CanMarshalSystemType()
		{
			var instance = CreateRemoteInstance();
			Assert.AreEqual("System.String", instance.GetTypeName(typeof(System.String)));
		}

		[Test]
		public void CanMarshalNullReference()
		{
			var instance = CreateRemoteInstance();
			instance.RegisterCallback(null);
		}

		[Test]
		public void CodeIsReallyExecutedRemotely()
		{
			var server = CreateRemoteInstance();
			var client = new MarshallableClass();
			Assert.AreNotEqual(server.GetCurrentProcessId(), client.GetCurrentProcessId());
		}

		[Test]
		public void RefArgumentWorks()
		{
			var server = CreateRemoteInstance();
			int aValue = 4;
			server.UpdateArgument(ref aValue);
			Assert.AreEqual(6, aValue);
		}

		[Test]
		public void CanRegisterCallbackInterface()
		{
			var server = CreateRemoteInstance();
			// Tests whether the return channel works, by providing an instance of a class to the server where
			// the actual object lives on the client.
			var cbi = new CallbackImpl();
			Assert.False(cbi.HasBeenCalled);
			server.RegisterCallback(cbi);
			server.DoCallback();
			server.RegisterCallback(null);
			Assert.True(cbi.HasBeenCalled);
		}

		[Test]
		public void DoesNotThrowExceptionIfAttemptingToRegisterPrivateMethodAsEventSink()
		{
			var server = CreateRemoteInstance();
			var objectWithEvent = server.GetInterface<IMyComponentInterface>();
			_dataReceived = null;
			Assert.DoesNotThrow(() => objectWithEvent.TimeChanged += ObjectWithEventOnTimeChangedPrivate);
		}

		[Test]
		public void CanRegisterEvent()
		{
			var server = CreateRemoteInstance();
			var objectWithEvent = server.GetInterface<IMyComponentInterface>();
			_dataReceived = null;
			objectWithEvent.TimeChanged += ObjectWithEventOnTimeChanged;
			objectWithEvent.StartTiming();
			int ticks = 100;
			while (string.IsNullOrEmpty(_dataReceived) && ticks-- > 0)
			{
				Thread.Sleep(100);
			}

			Assert.That(ticks > 0);
			Assert.False(string.IsNullOrWhiteSpace(_dataReceived));
			objectWithEvent.StopTiming();
			objectWithEvent.TimeChanged -= ObjectWithEventOnTimeChanged;
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
			var arguments = new ConstructorArgument(new ReferencedComponent() { ComponentName = "ClientUnderTest" });
			var service = _client.CreateRemoteInstance<ServiceClass>(arguments);

			// This calls the server, who calls back into the client. So we get something that the client generated
			string roundTrippedAnswer = service.DoSomething();

			Assert.AreEqual("Wrapped by Server: ClientUnderTest", roundTrippedAnswer);

		}

		[Test]
		public void UseMixedInstanceAsArgument()
		{
			var server = CreateRemoteInstance();
			var reference = new ReferencedComponent() { Data = 10 };
			// This is a serializable class that has a MarshalByRef member
			SerializableClassWithMarshallableMembers sc = new SerializableClassWithMarshallableMembers(1, reference);

			int reply = server.UseMixedArgument(sc);

			Assert.AreEqual(10, reply);

			reply = sc.CallbackViaComponent();

			Assert.AreEqual(10, reply);

			reference.Data = 20;
			reply = sc.CallbackViaComponent();
			Assert.AreEqual(20, reply);

			var sc2 = sc.ReturnSelfToCaller();

			Assert.True(ReferenceEquals(sc, sc2));
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
			var name = new AssemblyName("System.Management, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
			var assembly = Assembly.Load(name);
			Type t = assembly.GetType("System.Management.ManagementObjectSearcher", true);
			var instance = Activator.CreateInstance(t, "SELECT * FROM Win32_BIOS");

			Assert.IsNotNull(instance);
			((IDisposable)instance).Dispose();
		}

		[Test]
		public void UseRemoteSystemManagement()
		{
			var bios = _client.CreateRemoteInstance<CheckBiosVersion>();

			string[] versions = bios.GetBiosVersions();
			Console.WriteLine($"Server bios versions are: {string.Join(", ", versions)}.");
		}

		[Test]
		public void GetListOfMarshalByRefInstances()
		{
			var c = CreateRemoteInstance();
			var list = c.GetSomeComponents();
			Assert.That(list is List<ReferencedComponent>);
			Assert.AreEqual(2, list.Count);
			Assert.That(list[0].ComponentName == list[1].ComponentName);
		}

		[Test]
		public void HandleRemoteException()
		{
			var c = CreateRemoteInstance();
			bool didThrow = true;
			try
			{
				c.MaybeThrowException(0);
				didThrow = false;
			}
			catch (DivideByZeroException x)
			{
				Assert.IsNotNull(x);
				Console.WriteLine(x);
			}

			Assert.That(didThrow);
		}

		[Test]
		public void GetRemotingServerService()
		{
			var serverService = _client.RequestRemoteInstance<IRemoteServerService>();
			Assert.That(serverService.Ping());
		}

		[Test]
		public void SerializeUnserializableArgument()
		{
			var cls = CreateRemoteInstance();
			Assert.Throws<SerializationException>(() => cls.CallerError(new UnserializableObject()));
		}

		[Test]
		public void SerializeUnserializableReturnType()
		{
			var cls = CreateRemoteInstance();
			Assert.Throws<SerializationException>(() => cls.ServerError());
		}

		[Test]
		public void TwoClientsCanConnect()
		{
			var client2 = new Client("localhost", Client.DefaultNetworkPort);
			client2.Start();

			var firstInstance = _client.CreateRemoteInstance<MarshallableClass>();
			var secondInstance = client2.CreateRemoteInstance<MarshallableClass>();
			Assert.AreNotEqual(firstInstance, secondInstance);

			// Test that the callback ends on the correct client.
			var cb1 = new CallbackImpl();
			var cb2 = new CallbackImpl();
			firstInstance.RegisterCallback(cb1);
			secondInstance.RegisterCallback(cb2);
			firstInstance.DoCallback();
			Assert.That(cb1.HasBeenCalled);
			Assert.False(cb2.HasBeenCalled);

			client2.Disconnect();
			client2.Dispose();

			// This is still operational
			Assert.That(firstInstance.GetSomeData() != 0);
		}

		[Test]
		public void CanRegisterUnregisterEvents()
		{
			IMarshallInterface instance = _client.CreateRemoteInstance<MarshallableClass>();

			_dataReceived = null;
			instance.DoCallbackOnEvent("Test string");

			Assert.IsNull(_dataReceived);
			instance.AnEvent += CallbackMethod;
			instance.DoCallbackOnEvent("Another test string");
			Assert.False(string.IsNullOrWhiteSpace(_dataReceived));
			_dataReceived = null;
			instance.AnEvent -= CallbackMethod;
			instance.DoCallbackOnEvent("A final test string");
			Assert.IsNull(_dataReceived);
		}

		[Test]
		public void CanRegisterUnregisterEventWithoutAffectingOtherInstance()
		{
			IMarshallInterface instance = _client.CreateRemoteInstance<MarshallableClass>();
			IMarshallInterface instance2 = _client.CreateRemoteInstance<MarshallableClass>();

			_dataReceived = null;
			instance.DoCallbackOnEvent("Initial test string");

			Assert.IsNull(_dataReceived);
			instance.AnEvent += CallbackMethod;
			instance2.AnEvent += CallbackMethod;
			_dataReceived = null;
			instance.AnEvent -= CallbackMethod;
			instance.DoCallbackOnEvent("More testing");
			Assert.IsNotNull(_dataReceived);
		}

		[Test]
		public void RemovingAnAlreadyRemovedDelegateDoesNothing()
		{
			IMarshallInterface instance = _client.CreateRemoteInstance<MarshallableClass>();

			_dataReceived = null;
			instance.DoCallbackOnEvent("Test string");

			Assert.IsNull(_dataReceived);
			instance.AnEvent += CallbackMethod;
			instance.DoCallbackOnEvent("Another test string");
			Assert.False(string.IsNullOrWhiteSpace(_dataReceived));
			_dataReceived = null;
			instance.AnEvent -= CallbackMethod;
			instance.DoCallbackOnEvent("A third test string");
			Assert.IsNull(_dataReceived);
			instance.AnEvent -= CallbackMethod;
			instance.DoCallbackOnEvent("Test string 4");
			Assert.IsNull(_dataReceived);
		}

		private MarshallableClass CreateRemoteInstance()
		{
			return _client.CreateRemoteInstance<MarshallableClass>();
		}

		public void CallbackMethod(string argument)
		{
			_dataReceived = argument;
		}

		private sealed class CallbackImpl : MarshalByRefObject, ICallbackInterface
		{
			public CallbackImpl()
			{
				HasBeenCalled = false;
			}

			public bool HasBeenCalled
			{
				get;
				set;
			}

			public void FireSomeAction(string nameOfAction)
			{
				HasBeenCalled = true;
			}
		}
	}
}
