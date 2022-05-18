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
			Assert.IsNotNull(_serverProcess);

			// Port is currently hardcoded
			_client = new Client("localhost", Client.DefaultNetworkPort);
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
			Assert.AreNotEqual(Environment.ProcessId, firstImpl.GetCurrentProcessId());
			Assert.AreNotEqual(Environment.ProcessId, secondImpl.GetCurrentProcessId());
			Assert.AreNotEqual(firstImpl.GetCurrentProcessId(), secondImpl.GetCurrentProcessId());
		}

		[Test]
		public void SendArguments()
		{
			var firstServer = GetTransientServer();
			var secondImpl = firstServer.CreateTransientClass<MarshallableClass>();
			Assert.AreEqual(30, secondImpl.AddValues(10, 20));

			MemoryStream ms = new MemoryStream();
			byte[] data = new byte[10 * 1024 * 1024];
			data[1] = 2;
			ms.Write(data);
			Stopwatch sw = Stopwatch.StartNew();
			Assert.True(secondImpl.StreamDataContains(ms, 2));
		}

		[Test]
		public void RegisterCallback()
		{
			var firstServer = GetTransientServer();
			var secondImpl = firstServer.CreateTransientClass<MarshallableClass>();
			var cb = new TransientCallbackImpl();
			secondImpl.RegisterCallback(cb);
			secondImpl.DoCallback();
			Assert.True(cb.HasCallbackOccurred);
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

		[Test]
		public void GetWithManyInterfaces()
		{
			_transientServer = _client.CreateRemoteInstance<TransientServer>();
			_transientServer.Init(Client.DefaultNetworkPort + 2);
			IDisposable ds = _transientServer.CreateTransientClass<WithManyInterfaces>();
			Assert.IsNotNull(ds);
			Assert.That(ds is IMarshallInterface);
			ds.Dispose();

			IMarshallInterface im = _transientServer.CreateTransientClass<WithManyInterfaces>();
			Assert.IsNotNull(im);
			Assert.IsNotNull(im.StringProcessId());
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
