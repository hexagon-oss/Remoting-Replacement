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
	public class ServerTerminationTest
	{
		private Process _serverProcess;
		private Client _client;

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
			if (_client != null)
			{
				_client.ShutdownServer();
				_client.Dispose();
				_client = null;
			}

			if (_serverProcess != null)
			{
				_serverProcess.WaitForExit(2000);
				_serverProcess.Kill(); // May be necessary here
				_serverProcess.WaitForExit();
				_serverProcess.Dispose();
				_serverProcess = null;
			}
		}

		[Test]
		public void TestShuttingDownServer()
		{
			var server = _client.RequestRemoteInstance<IRemoteServerService>();
			Assert.That(server.Ping());
			server.TerminateRemoteServerService();
			bool didThrow = false;
			try
			{
				server.Ping();
			}
			catch (RemotingException)
			{
				didThrow = true;
			}

			Assert.That(didThrow);
			didThrow = false;

			try
			{
				_client.RequestRemoteInstance<IRemoteServerService>();
			}
			catch (RemotingException)
			{
				didThrow = true;
			}

			Assert.That(didThrow);
			didThrow = false;
			try
			{
				_client.CreateRemoteInstance<TestDummy>();
			}
			catch (RemotingException)
			{
				didThrow = true;
			}

			Assert.That(didThrow);
		}

		public class TestDummy : MarshalByRefObject
		{
		}
	}
}
