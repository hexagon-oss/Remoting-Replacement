using System;
using System.Diagnostics;
using System.Threading;
using NewRemoting;
using NewRemoting.Toolkit;
using NUnit.Framework;
using SampleServerClasses;

namespace NewRemotingUnitTest
{
	[TestFixture(true)]
	[TestFixture(false)]
	[NonParallelizable]
	internal sealed class CrossAppDomainCancellationTokenSourceTest
	{
		private Process _serverProcess;
		private Client _client;
		private AuthenticationHelper _helper;

		public CrossAppDomainCancellationTokenSourceTest(bool withAuthentication)
		{
			if (withAuthentication)
			{
				_helper = new AuthenticationHelper();
			}
		}

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			_helper?.SetUp();
			_serverProcess = Process.Start("RemotingServer.exe");
			Assert.That(_serverProcess, Is.Not.Null);

			// Port is currently hardcoded
			_client = new Client("localhost", Client.DefaultNetworkPort, _helper == null ? null : new AuthenticationInformation(_helper.CertificateFileName, _helper.CertificatePassword), new ConnectionSettings());
			_client.Start();
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_helper?.TearDown();

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

		[Test]
		public void SingleAppDomainCancellation()
		{
			using (var cts = new CrossAppDomainCancellationTokenSource())
			{
				Assert.That(!cts.IsCancellationRequested);
				var localToken = cts.Token.GetLocalCancellationToken();
				Assert.That(localToken, Is.Not.EqualTo(CancellationToken.None));
				Assert.That(!localToken.IsCancellationRequested);
				cts.Cancel();
				Assert.That(localToken.IsCancellationRequested);
				Assert.That(cts.IsCancellationRequested);
			}
		}

		[Test]
		public void CrossAppDomainCancellation()
		{
			var dummy = _client.CreateRemoteInstance<DummyCancellableType>();
			using (var cts = new CrossAppDomainCancellationTokenSource())
			{
				Assert.That(!cts.IsCancellationRequested);
				cts.Cancel();
				Assert.Throws<OperationCanceledException>(() => dummy.DoSomething(cts.Token));
				Assert.That(cts.IsCancellationRequested);
			}
		}

		[Test]
		public void CrossAppDomainCancellationWithTimeout()
		{
			var dummy = _client.CreateRemoteInstance<DummyCancellableType>();
			using (var cts = new CrossAppDomainCancellationTokenSource())
			{
				Assert.That(!cts.IsCancellationRequested);
				cts.CancelAfter(TimeSpan.FromSeconds(0.2));
				Assert.Throws<OperationCanceledException>(() => dummy.WaitForToken(cts.Token));
				Assert.That(cts.IsCancellationRequested);
			}
		}

		[Test]
		public void CrossAppDomainCancellationWithoutCancellation()
		{
			var dummy = _client.CreateRemoteInstance<DummyCancellableType>();
			using (var cts = new CrossAppDomainCancellationTokenSource())
			{
				Assert.That(!cts.IsCancellationRequested);
				dummy.DoSomething(cts.Token);
				Assert.That(cts.IsCancellationRequested, Is.False);
			}
		}

		[Test]
		public void UseCancellationWithWrongTokenSource()
		{
			Stopwatch sw = Stopwatch.StartNew();
			var dummy = _client.CreateRemoteInstance<DummyCancellableType>();
			var cts = new CancellationTokenSource();

			var token = cts.Token;
			Assert.That(!cts.IsCancellationRequested);
			cts.Cancel();
			Assert.Throws<OperationCanceledException>(() => dummy.DoSomethingWithNormalToken(token)); // No crash, since already cancelled
			Assert.That(cts.IsCancellationRequested);
			cts.Dispose();

			// Note: If we use cts.Token here, it will throw ObjectDisposedException right away. But we want to see whether
			// the server can handle the case where the token source is already disposed, so we use the token that we got before disposing the source.
			Assert.Throws<OperationCanceledException>(() => dummy.DoSomethingWithNormalToken(token));
		}

		[Test]
		public void UseCancellationWithWrongToken()
		{
			Stopwatch sw = Stopwatch.StartNew();
			var dummy = _client.CreateRemoteInstance<DummyCancellableType>();
			var cts = new CancellationTokenSource();

			var token = cts.Token;
			Assert.Throws<InvalidOperationException>(() => dummy.DoSomethingWithNormalToken(token));
			Assert.That(cts.IsCancellationRequested, Is.False);
			cts.Dispose();
		}
	}
}
