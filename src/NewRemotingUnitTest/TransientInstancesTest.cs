using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewRemoting;
using NUnit.Framework;
using SampleServerClasses;

namespace NewRemotingUnitTest
{
	[TestFixture]
	public class TransientInstancesTest
	{
		private Process _serverProcess;
		private Client _client;
		private string _dataReceived;
		private ITransientServer _transientServer;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			_serverProcess = Process.Start("RemotingServer.exe");
			Assert.That(_serverProcess, Is.Not.Null);

			// Port is currently hardcoded
			_client = new Client("localhost", Client.DefaultNetworkPort, null, new ConnectionSettings());
			_client.Start();
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
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
				Assert.That(_serverProcess.WaitForExit(2000));
				_serverProcess.Kill();
				_serverProcess.Dispose();
				_serverProcess = null;
			}
		}

		[SetUp]
		public void SetUp()
		{
			_dataReceived = null;
		}

		[TearDown]
		public void TearDown()
		{
			if (_transientServer != null)
			{
				_client.ForceGc();
				_transientServer.Dispose();
				_transientServer = null;
			}
		}

		[Test]
		public void SimpleTestCase()
		{
			var firstServer = GetTransientServer();
			var firstImpl = _client.CreateRemoteInstance<MarshallableClass>();
			var secondImpl = firstServer.CreateTransientClass<MarshallableClass>();
			Assert.That(firstImpl.GetCurrentProcessId(), Is.Not.EqualTo(Environment.ProcessId));
			Assert.That(secondImpl.GetCurrentProcessId(), Is.Not.EqualTo(Environment.ProcessId));
			Assert.That(secondImpl.GetCurrentProcessId(), Is.Not.EqualTo(firstImpl.GetCurrentProcessId()));
		}

		[Test]
		public void SendArguments()
		{
			var firstServer = GetTransientServer();
			var secondImpl = firstServer.CreateTransientClass<MarshallableClass>();
			Assert.That(secondImpl.AddValues(10, 20), Is.EqualTo(30));

			MemoryStream ms = new MemoryStream();
			byte[] data = new byte[10 * 1024 * 1024];
			data[1] = 2;
			ms.Write(data);
			Stopwatch sw = Stopwatch.StartNew();
			Assert.That(secondImpl.StreamDataContains(ms, 2), Is.True);
		}

		[Test]
		public void RegisterCallback()
		{
			var firstServer = GetTransientServer();
			var secondImpl = firstServer.CreateTransientClass<MarshallableClass>();
			var cb = new TransientCallbackImpl();
			secondImpl.RegisterCallback(cb);
			secondImpl.DoCallback();
			Assert.That(cb.HasCallbackOccurred, Is.True);
			secondImpl.RegisterCallback(null);
		}

		public void CallbackMethod(string argument, string sender)
		{
			_dataReceived = argument;
		}

		[Test]
		public void RegisterEvent()
		{
			var server = GetTransientServer();
			IMarshallInterface instance = server.CreateTransientClass<MarshallableClass>();

			_dataReceived = null;
			instance.DoCallbackOnEvent("Test string");

			Assert.That(_dataReceived, Is.Null);
			instance.AnEvent2 += CallbackMethod;
			instance.DoCallbackOnEvent("Another test string");
			Assert.That(string.IsNullOrWhiteSpace(_dataReceived), Is.False);
			_dataReceived = null;
			instance.AnEvent2 -= CallbackMethod;
			instance.DoCallbackOnEvent("A third test string");
			Assert.That(_dataReceived, Is.Null);
			instance.AnEvent2 -= CallbackMethod;
			instance.DoCallbackOnEvent("Test string 4");
			Assert.That(_dataReceived, Is.Null);
		}

		[Test]
		public void GetWithManyInterfaces()
		{
			_transientServer = _client.CreateRemoteInstance<TransientServer>();
			_transientServer.Init(Client.DefaultNetworkPort + 2);
			IDisposable ds = _transientServer.CreateTransientClass<WithManyInterfaces>();
			Assert.That(ds, Is.Not.Null);
			Assert.That(ds is IMarshallInterface);
			ds.Dispose();

			IMarshallInterface im = _transientServer.CreateTransientClass<WithManyInterfaces>();
			Assert.That(im, Is.Not.Null);
			Assert.That(im.StringProcessId(), Is.Not.Null);
			ds = (IDisposable)im;
			ds.Dispose();
		}

		private ITransientServer GetTransientServer()
		{
			_transientServer = _client.CreateRemoteInstance<TransientServer>();
			_transientServer.Init(Client.DefaultNetworkPort + 2);
			var ids = _client.InstanceIdentifiers();
			Debug.WriteLine($"Local instance id is {ids.Local}. Remote instance id is {ids.Remote}");
			return _transientServer;
		}

		internal sealed class TransientCallbackImpl : MarshalByRefObject, ICallbackInterface
		{
			public TransientCallbackImpl()
			{
				HasCallbackOccurred = false;
			}

			public bool HasCallbackOccurred
			{
				get;
				private set;
			}

			public event Action<string> Callback;

			public void FireSomeAction(string nameOfAction)
			{
				HasCallbackOccurred = true;
			}

			public void InvokeCallback(string data)
			{
				Callback?.Invoke(data);
			}
		}
	}
}
