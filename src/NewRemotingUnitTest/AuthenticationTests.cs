using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System;
using System.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	[NonParallelizable]
	public class AuthenticationTests
	{
		private AuthenticationHelper _helper = new();

		[SetUp]
		public void SetUp()
		{
			_helper.SetUp();
		}

		[TearDown]
		public void TearDown()
		{
			_helper.TearDown();

		}

		[Test]
		public void AuthenticationSuccessful()
		{
			FileInfo fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
			var psi = new ProcessStartInfo(Path.Combine(fi.DirectoryName, "RemotingServer.exe"));

			using (var serverProcess = Process.Start(psi))
			{
				Assert.IsNotNull(serverProcess);
				Assert.False(string.IsNullOrEmpty(_helper.CertificateFileName));
				using (var client = new Client("localhost", Client.DefaultNetworkPort, new AuthenticationInformation(_helper.CertificateFileName, _helper.CertificatePassword), new ConnectionSettings()))
				{
					Assert.DoesNotThrow(() => client.Start());
				}

				serverProcess.Kill(true);
				serverProcess.WaitForExit(2000);
			}
		}

		[Test]
		public void AuthenticationOfClientWrongPassword()
		{
			FileInfo fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
			var psi = new ProcessStartInfo(Path.Combine(fi.DirectoryName, "RemotingServer.exe"));

			Debug.WriteLine($"Attempting to start {psi.FileName}...");
			Debug.WriteLine($"Current directory is {Environment.CurrentDirectory}");

			using (var serverProcess = Process.Start(psi))
			{
				Assert.IsNotNull(serverProcess);
				Assert.False(string.IsNullOrEmpty(_helper.CertificateFileName));
				var badPassword = new AuthenticationInformation(_helper.CertificateFileName, "wrongpassword");

				Assert.Catch<CryptographicException>(() => new Client("localhost", Client.DefaultNetworkPort, badPassword, new ConnectionSettings()), "The specified network password is not correct");

				serverProcess.Kill(true);
				serverProcess.WaitForExit(2000);
			}
		}

		[Test]
		public void AuthenticationOfClientCertificateNotInStoreWorks()
		{
			FileInfo fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
			var psi = new ProcessStartInfo(Path.Combine(fi.DirectoryName, "RemotingServer.exe"));

			using (var serverProcess = Process.Start(psi))
			{
				Assert.IsNotNull(serverProcess);
				Assert.False(string.IsNullOrEmpty(_helper.CertificateFileName));
				Client client = new Client("localhost", Client.DefaultNetworkPort, new AuthenticationInformation(_helper.CertificateFileName, _helper.CertificatePassword), new ConnectionSettings());
				client.Dispose();

				serverProcess.Kill(true);
				serverProcess.WaitForExit(2000);
			}
		}

		[Test]
		public void AuthenticationOfClientDifferentCertificate()
		{
			FileInfo fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
			var psi = new ProcessStartInfo(Path.Combine(fi.DirectoryName, "RemotingServer.exe"));
			var configAndAuthenticationClient = AuthenticationHelper.CreateAuthenticationInfo();
			var configAndAuthenticationServer = AuthenticationHelper.CreateAuthenticationInfo();

			using (var serverProcess = Process.Start(psi))
			{
				Assert.IsNotNull(serverProcess);
				Assert.IsNotNull(configAndAuthenticationClient.Item2);
				Assert.That(string.IsNullOrEmpty(configAndAuthenticationClient.Item2.CertificateFileName), Is.EqualTo(false));
				string msg = "Unable to write data to the transport connection: An established connection was aborted by the software in your host machine..";
				Assert.Catch<IOException>(() => new Client("localhost", Client.DefaultNetworkPort, configAndAuthenticationClient.Item2, new ConnectionSettings()), msg);

				serverProcess.Kill(true);
				serverProcess.WaitForExit(2000);
			}
		}
	}
}
