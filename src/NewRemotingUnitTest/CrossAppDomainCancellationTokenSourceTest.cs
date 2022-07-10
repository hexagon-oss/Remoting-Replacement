using System;
using System.Diagnostics;
using System.Threading;
using NewRemoting;
using NewRemoting.Toolkit;
using NUnit.Framework;
using SampleServerClasses;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal sealed class CrossAppDomainCancellationTokenSourceTest
	{
		private Process _serverProcess;
		private Client _client;

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
		public void SingleAppDomainCancellation()
		{
			using (var cts = new CrossAppDomainCancellationTokenSource())
			{
				Assert.IsFalse(cts.IsCancellationRequested);
				var localToken = cts.Token.GetLocalCancellationToken();
				Assert.AreNotEqual(CancellationToken.None, localToken);
				Assert.IsFalse(localToken.IsCancellationRequested);
				cts.Cancel();
				Assert.IsTrue(localToken.IsCancellationRequested);
				Assert.IsTrue(cts.IsCancellationRequested);
			}
		}

		[Test]
		public void CrossAppDomainCancellation()
		{
			var dummy = _client.CreateRemoteInstance<DummyCancellableType>();
			using (var cts = new CrossAppDomainCancellationTokenSource())
			{
				Assert.IsFalse(cts.IsCancellationRequested);
				cts.Cancel();
				Assert.Throws<OperationCanceledException>(() => dummy.DoSomething(cts.Token));
				Assert.IsTrue(cts.IsCancellationRequested);
			}
		}

		[Test]
		public void CrossAppDomainCancellation2()
		{
			var dummy = _client.CreateRemoteInstance<DummyCancellableType>();
			using (var cts = new CrossAppDomainCancellationTokenSource())
			{
				Assert.IsFalse(cts.IsCancellationRequested);
				cts.CancelAfter(TimeSpan.FromSeconds(0.2));
				Assert.Throws<OperationCanceledException>(() => dummy.DoSomethingWithNormalToken(cts.Token));
				Assert.IsTrue(cts.IsCancellationRequested);
			}
		}
	}
}
