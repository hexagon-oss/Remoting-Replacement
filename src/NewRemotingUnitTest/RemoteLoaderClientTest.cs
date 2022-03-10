using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
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
		[TestFixture]
		public sealed class LocalExecution
		{
			[Test]
			public void CorrectlyHandleAlreadyExistingRemoteLoader()
			{
				string arguments = string.Empty; // TODO: Fix
				var existingProcess = PaExecClient.CreateProcessLocal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RemoteLoaderWindowsClient.REMOTELOADER_EXECUTABLE), arguments);
				existingProcess.Start();
				Assert.IsNotNull(existingProcess);
				Thread.Sleep(2000);
				Assert.That(existingProcess.HasExited == false);

				CancellationTokenSource errorTokenSource = new CancellationTokenSource();
				errorTokenSource.CancelAfter(TimeSpan.FromSeconds(60));
				IRemoteLoaderClient client = new RemoteLoaderWindowsClient(Credentials.None, "127.0.0.1", Client.DefaultNetworkPort, new FileHashCalculator(), f => true, TimeSpan.FromSeconds(2));
				client.Connect(errorTokenSource.Token);
				// Should not be cancelled yet.
				Assert.False(errorTokenSource.IsCancellationRequested);
				existingProcess.Kill();
				client.Dispose();
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
				IRemoteLoaderClient client = new RemoteLoaderWindowsClient(Credentials.None, "127.0.0.1", Client.DefaultNetworkPort, new FileHashCalculator(), f => true, TimeSpan.FromSeconds(0.1));
				try
				{
					CancellationTokenSource errorTokenSource = new CancellationTokenSource();
					errorTokenSource.CancelAfter(TimeSpan.FromSeconds(20));
					client.Connect(errorTokenSource.Token);
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
			private static readonly Credentials RemoteCredentials = Credentials.Create("Administrator", "Administrator", RemoteHost, null);

			[Test]
			public void LocalHostDirectoryInfo()
			{
				using (IRemoteLoaderClient remoteLoaderClient = new RemoteLoaderFactory().Create(RemoteCredentials, "127.0.0.1"))
				{
					remoteLoaderClient.Connect(CancellationToken.None);
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
					remoteLoaderClient.Connect(CancellationToken.None);
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
					remoteLoaderClient.Connect(CancellationToken.None);
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
				using (IRemoteLoaderClient remoteLoaderClient1 = new RemoteLoaderFactory().Create(Credentials.None, "127.0.0.1"))
				{
					remoteLoaderClient1.Connect(CancellationToken.None);

					using (IRemoteLoaderClient remoteLoaderClient2 = new RemoteLoaderFactory().Create(Credentials.None, "127.0.0.1"))
					{
						remoteLoaderClient2.Connect(CancellationToken.None);

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
