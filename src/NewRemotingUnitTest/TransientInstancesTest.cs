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
		private ITransientServer _firstServer;

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

		[TearDown]
		public void TearDown()
		{
			if (_firstServer != null)
			{
				_firstServer.Dispose();
				_firstServer = null;
			}
		}

		[Test]
		public void SimpleTestCase()
		{
			_firstServer = GetTransientServer();
			var firstImpl = _client.CreateRemoteInstance<MarshallableClass>();
			var secondImpl = _firstServer.CreateTransientClass<MarshallableClass>();
			Assert.AreNotEqual(Environment.ProcessId, firstImpl.GetCurrentProcessId());
			Assert.AreNotEqual(Environment.ProcessId, secondImpl.GetCurrentProcessId());
			Assert.AreNotEqual(firstImpl.GetCurrentProcessId(), secondImpl.GetCurrentProcessId());
		}

		[Test]
		public void SendArguments()
		{
			_firstServer = GetTransientServer();
			var secondImpl = _firstServer.CreateTransientClass<MarshallableClass>();
			Assert.AreEqual(30, secondImpl.AddValues(10, 20));

			MemoryStream ms = new MemoryStream();
			byte[] data = new byte[10 * 1024 * 1024];
			data[1] = 2;
			ms.Write(data);
			Stopwatch sw = Stopwatch.StartNew();
			Assert.True(secondImpl.StreamDataContains(ms, 2));
		}

		private ITransientServer GetTransientServer()
		{
			ITransientServer server = _client.CreateRemoteInstance<TransientServer>();
			server.Init(Client.DefaultNetworkPort + 2);
			return server;
		}
	}
}
