using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	public sealed class RemoteLoaderClientTest
	{
		/// <summary>
		/// Needs to be public to be accessible from remote loader process
		/// </summary>
		[TestFixture(true)]
		[TestFixture(false)]
		[NonParallelizable]
		public sealed class LocalExecution
		{
			private static Credentials _remoteCredentials = Credentials.Create("Administrator", "Administrator", "localhost", null);
			private AuthenticationHelper _helper;
			private AuthenticationInformation _authenticationInfo;

			public LocalExecution(bool withCertificate)
			{
				if (withCertificate)
				{
					_helper = new AuthenticationHelper();
				}
			}

			[OneTimeSetUp]
			public void OneTimeSetUp()
			{
				_helper?.SetUp();
				_authenticationInfo = _helper == null ? null : new AuthenticationInformation(_helper.CertificateFileName, _helper.CertificatePassword);
				_remoteCredentials = Credentials.Create("Administrator", "Administrator", "localhost", _authenticationInfo);
			}

			[OneTimeTearDown]
			public void OneTimeTearDown()
			{
				_helper?.TearDown();
			}

			[Test]
			public void CorrectlyHandleAlreadyExistingRemoteLoader()
			{
				string withAuth = _helper != null ? "with" : "none";
				var now = DateTime.UtcNow.ToString("yyyyMMdd_hhmmsf");
				string logFile = AppDomain.CurrentDomain.BaseDirectory + $"../../../../../artifacts/CorrectlyHandleAlreadyExistingRemoteLoader_{now}_{withAuth}.log";
				string clientFile = AppDomain.CurrentDomain.BaseDirectory + $"../../../../../artifacts/CorrectlyHandleAlreadyExistingRemoteLoader_client_{now}_{withAuth}.log";
				using SimpleLogFileWriter clientLogger = new SimpleLogFileWriter(clientFile, "client");
				string arguments = $"-v -l {logFile}";
				var existingProcess = PaExecClient.CreateProcessLocal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RemoteLoaderWindowsClient.REMOTELOADER_EXECUTABLE), arguments);
				existingProcess.Start();
				Assert.IsNotNull(existingProcess);
				Thread.Sleep(2000);
				Assert.That(existingProcess.HasExited == false);

				CancellationTokenSource errorTokenSource = new CancellationTokenSource();
				errorTokenSource.CancelAfter(TimeSpan.FromSeconds(60));
				logFile = AppDomain.CurrentDomain.BaseDirectory + $"../../../../../artifacts/CorrectlyHandleAlreadyExistingRemoteLoader_second_instance_{now}.log";
				arguments = $"-v -l {logFile}";

				string remoteLoaderClientLogFile = AppDomain.CurrentDomain.BaseDirectory + $"../../../../../artifacts/CorrectlyHandleAlreadyExistingRemoteLoader_windowsRemotingClient_{now}_{withAuth}.log";
				using SimpleLogFileWriter remoteLoaderClientLogger = new SimpleLogFileWriter(remoteLoaderClientLogFile, "WindowsRemotingClient");

				using IRemoteLoaderClient client = new RemoteLoaderWindowsClient(_remoteCredentials, "127.0.0.1", Client.DefaultNetworkPort, new FileHashCalculator(), f => true, TimeSpan.FromSeconds(2), arguments, remoteLoaderClientLogger);
				try
				{
				client.Connect(errorTokenSource.Token, clientLogger);
				clientLogger.LogInformation("Client connect done");
				}
				catch (Exception e)
				{
					clientLogger.LogError($"connect has thrown exception {e.Message}");
				}

				// Should not be cancelled yet.
				Assert.False(errorTokenSource.IsCancellationRequested);
				existingProcess.Kill();
			}

			/// <summary>
			/// This test ensures we're not using the "default" way of implementing remoting timeouts.
			/// The suggested way would be to set a timeout when the channel is registered, but
			/// that timeout would be valid for all calls using that object, not only the connection,
			/// so that any remoting call which takes longer than the timeout fails with an error.
			/// This situation might be acceptable for internet services, but it's not in our case.
			/// </summary>
			[Test]
			public void NoCrashesWhenRemoteCallsAreExpensive()
			{
				IRemoteLoaderClient client = new RemoteLoaderWindowsClient(_remoteCredentials, "127.0.0.1", Client.DefaultNetworkPort, new FileHashCalculator(), f => true, TimeSpan.FromSeconds(0.1), string.Empty);
				try
				{
					CancellationTokenSource errorTokenSource = new CancellationTokenSource();
					errorTokenSource.CancelAfter(TimeSpan.FromSeconds(20));
					client.Connect(errorTokenSource.Token, null);
					Assert.False(errorTokenSource.IsCancellationRequested);
					RemoteObjectWithSlowMethods remObject = client.CreateObject<RemoteObjectWithSlowMethods>();
					remObject.Sleep(TimeSpan.FromSeconds(1));
					// Also ensure we're not accidentally running the client in our own process.
					Assert.That(remObject.ProcessId() != Process.GetCurrentProcess().Id);
				}
				finally
				{
					client.Dispose();
				}
			}

			/// <summary>
			/// When a socket exception happens using a remote function, a RemotingException should be thrown instead
			/// </summary>
			[Test]
			public void SocketExceptionsAreWrappedCorrectly()
			{
				IRemoteLoaderClient client = new RemoteLoaderWindowsClient(_remoteCredentials, "127.0.0.1", Client.DefaultNetworkPort, new FileHashCalculator(), f => true, TimeSpan.FromSeconds(0.1), string.Empty);
				try
				{
					CancellationTokenSource errorTokenSource = new CancellationTokenSource();
					errorTokenSource.CancelAfter(TimeSpan.FromSeconds(20));
					client.Connect(errorTokenSource.Token, null);
					Assert.False(errorTokenSource.IsCancellationRequested);
					client.RemoteClient.Dispose(); // This should not normally be done, but we want RemoteLoaderWindowsClient to keep the valid reference here
					Assert.Throws<RemotingException>(() => client.CreateObject<RemoteObjectWithSlowMethods>());
				}
				finally
				{
					client.Dispose();
				}
			}

			[Test]
			public void ConnectCheckExistingInstance()
			{
				using (IRemoteLoaderClient client = new RemoteLoaderWindowsClient(_remoteCredentials, "127.0.0.1", Client.DefaultNetworkPort, new FileHashCalculator(), f => true, TimeSpan.FromSeconds(2), string.Empty))
				{
					CancellationTokenSource errorTokenSource = new CancellationTokenSource();
					Assert.IsTrue(client.Connect(true, errorTokenSource.Token, null));
				}
			}

			[Test]
			public void ConnectCheckExistingInstanceProcessExist()
			{
				using (IRemoteLoaderClient client = new RemoteLoaderWindowsClient(_remoteCredentials, "127.0.0.1", Client.DefaultNetworkPort, new FileHashCalculator(), f => true, TimeSpan.FromSeconds(2), string.Empty))
				{
					CancellationTokenSource errorTokenSource = new CancellationTokenSource();
					Assert.IsTrue(client.Connect(true, errorTokenSource.Token, null));
					Assert.IsFalse(client.Connect(true, errorTokenSource.Token, null));
				}
			}

			/// <summary>
			/// Needs to be public to be accessible from remote loader process
			/// </summary>
			public class RemoteObjectWithSlowMethods : MarshalByRefObject
			{
				public RemoteObjectWithSlowMethods()
				{
				}

				public virtual void Sleep(TimeSpan time)
				{
					Thread.Sleep(time);
				}

				public virtual int ProcessId()
				{
					return Process.GetCurrentProcess().Id;
				}
			}
		}

		[TestFixture]
		[Explicit("Requires configured infrastructure")]
		// Needs to be public to be accessible from remote loader process
		public sealed class RemoteExecution
		{
			private static readonly string RemoteHost = "10.60.119.222";

			/// <summary>
			/// If you cannot login to the remote host even if the credentials are correct:
			/// "Failed to connect to Service Control Manager on XXX.XXX.XXX.XXX. Access is denied. [Err=0x5, 5]"
			/// make sure you do not have a user on the remote system which has the same username than currently active on the local machine.
			/// </summary>
			private static Credentials _remoteCredentials = Credentials.Create("Administrator", "Administrator", RemoteHost, null);

			public static Credentials RemoteCredentials { get => _remoteCredentials; set => _remoteCredentials = value; }

			[Test]
			public void LocalHostDirectoryInfo()
			{
				using (IRemoteLoaderClient remoteLoaderClient = new RemoteLoaderFactory().Create(RemoteCredentials, "127.0.0.1"))
				{
					remoteLoaderClient.Connect(CancellationToken.None, null);
					var dirInfo = remoteLoaderClient.CreateObject<DirectoryInfo>(new object[] { "C:\\" });
					var subDirs = dirInfo.GetDirectories();
					foreach (var subDir in subDirs)
					{
						Console.WriteLine(subDir.Name);
					}
				}
			}

			[Test]
			public void RemoteDirectoryInfo()
			{
				using (IRemoteLoaderClient remoteLoaderClient = new RemoteLoaderFactory().Create(RemoteCredentials, RemoteHost))
				{
					remoteLoaderClient.Connect(CancellationToken.None, null);
					var dirInfo = remoteLoaderClient.CreateObject<DirectoryInfo>(new object[] { "C:\\" });
					var subDirs = dirInfo.GetDirectories();
					foreach (var subDir in subDirs)
					{
						Console.WriteLine(subDir.Name);
					}
				}
			}

			/// <summary>
			/// Limit on XP is 10, on Win 7 20 active net sessions
			/// </summary>
			[Test]
			[Repeat(30)]
			public void RemoteDirectoryInfoRepeatedCheckNetSessionLimit()
			{
				RemoteDirectoryInfo();
			}

			[Test]
			public void RemoteDirectoryInfoWithoutDisposingShouldTerminateRemoteLoaderAfterWhile()
			{
				using (IRemoteLoaderClient remoteLoaderClient = new RemoteLoaderFactory().Create(RemoteCredentials, RemoteHost))
				{
					remoteLoaderClient.Connect(CancellationToken.None, null);
					var dirInfo = remoteLoaderClient.CreateObject<DirectoryInfo>(new object[] { "C:\\" });
					var subDirs = dirInfo.GetDirectories();
					foreach (var subDir in subDirs)
					{
						Console.WriteLine(subDir.Name);
					}
				}
			}

			[Test]
			public void AccessDifferentLocalRemotLoaderObjects()
			{
				using (IRemoteLoaderClient remoteLoaderClient1 = new RemoteLoaderFactory().Create(_remoteCredentials, "127.0.0.1"))
				{
					remoteLoaderClient1.Connect(CancellationToken.None, null);

					using (IRemoteLoaderClient remoteLoaderClient2 = new RemoteLoaderFactory().Create(_remoteCredentials, "127.0.0.1"))
					{
						remoteLoaderClient2.Connect(CancellationToken.None, null);

						var testObjectRemote2 = remoteLoaderClient2.CreateObject<TestObject>();
						var testObjectRemote1 = remoteLoaderClient1.CreateObject<TestObject>();
						var id = Guid.NewGuid().ToString();
						ServiceContainer.AddService(new TestObject());
						Assert.AreEqual(Process.GetCurrentProcess().Id, ServiceContainer.GetService<TestObject>().ProcessId);

						Assert.AreNotEqual(Process.GetCurrentProcess().Id, testObjectRemote1.ProcessId);
						Assert.AreNotEqual(Process.GetCurrentProcess().Id, testObjectRemote2.ProcessId);
						Assert.AreNotEqual(testObjectRemote1.ProcessId, testObjectRemote2.ProcessId);
					}
				}
			}

			[Test]
			public void ConnectCheckExistingInstanceCancellation()
			{
				using (IRemoteLoaderClient client = new RemoteLoaderFactory().Create(RemoteCredentials, RemoteHost))
				{
					// check if cancellation works, timeout here is shorter than the 10 seconds one in Connect implementation
					CancellationTokenSource timeoutCts = new CancellationTokenSource(100);
					Assert.Throws<OperationCanceledException>(() => client.Connect(true, timeoutCts.Token, null));
				}
			}

			[Test]
			public void ConnectCheckExistingInstanceInvalidRemote()
			{
				using (IRemoteLoaderClient client = new RemoteLoaderFactory().Create(RemoteCredentials, "wrongRemote"))
				{
					CancellationTokenSource errorTokenSource = new CancellationTokenSource();
					Assert.Throws<RemotingException>(() => client.Connect(true, errorTokenSource.Token, null));
				}
			}

			[Test]
			public void ConnectCheckExistingInstance()
			{
				using (IRemoteLoaderClient client = new RemoteLoaderFactory().Create(RemoteCredentials, RemoteHost))
				{
					CancellationTokenSource errorTokenSource = new CancellationTokenSource();
					Assert.IsTrue(client.Connect(true, errorTokenSource.Token, null));
				}
			}

			[Test]
			public void ConnectCheckExistingInstanceProcessExist()
			{
				using (IRemoteLoaderClient client = new RemoteLoaderFactory().Create(RemoteCredentials, RemoteHost))
				{
					CancellationTokenSource errorTokenSource = new CancellationTokenSource();
					Assert.IsTrue(client.Connect(true, errorTokenSource.Token, null));
					Assert.IsFalse(client.Connect(true, errorTokenSource.Token, null));
				}
			}

			/// <summary>
			/// Needs to be public to be accessible from remote loader process
			/// </summary>
			public sealed class TestObject : MarshalByRefObject
			{
				public int ProcessId
				{
					get
					{
						return Process.GetCurrentProcess().Id;
					}
				}
			}
		}
	}
}
