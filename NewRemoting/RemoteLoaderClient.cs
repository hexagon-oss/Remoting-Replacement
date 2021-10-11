using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;
using NewRemoting;
using NewRemoting.Toolkit;

namespace NewRemoting
{
	public class RemoteLoaderClient : PaExecClient, IRemoteLoaderClient
	{
		public const string REMOTELOADER_EXECUTABLE = "RemotingServer.exe";
		private const string REMOTELOADER_DIRECTORY = @"%temp%\RemoteLoader";
		private const string REMOTELOADER_DEPENDENCIES_FILENAME = REMOTELOADER_EXECUTABLE + ".dependencies.txt";

		private readonly Func<FileInfo, bool> _shouldFileBeUploadedFunc;
		private readonly string _remoteLoaderId;
		private readonly FileHashCalculator _fileHashCalculator;

		private IRemoteServerService _remoteLoader;
		private Client _remotingClient;

		public RemoteLoaderClient(Credentials remoteCredentials, string remoteHost)
			: this(remoteCredentials, remoteHost, (x) => true)
		{
		}

		public RemoteLoaderClient(Credentials remoteCredentials, string remoteHost, Func<FileInfo, bool> shouldFileBeUploadedFunc)
			: this(remoteCredentials, remoteHost, new FileHashCalculator(), shouldFileBeUploadedFunc, TimeSpan.FromSeconds(1))
		{
		}

		internal RemoteLoaderClient(Credentials remoteCredentials, string remoteHost,
			FileHashCalculator fileHashCalculator,
			Func<FileInfo, bool> shouldFileBeUploadedFunc, TimeSpan waitTimeBetweenPaExecExecute)
			: base(remoteCredentials, remoteHost, waitTimeBetweenPaExecExecute)
		{
			_shouldFileBeUploadedFunc = shouldFileBeUploadedFunc ?? throw new ArgumentNullException(nameof(shouldFileBeUploadedFunc));
			_remoteLoaderId = Guid.NewGuid().ToString();
			_fileHashCalculator = fileHashCalculator ?? throw new ArgumentNullException(nameof(fileHashCalculator));
			OutputDataReceived += (s, l) =>
			{
				if (l == LogLevel.Information)
				{
					Logger.LogInformation(s);
				}
				else
				{
					Logger.LogError(s);
				}
			};
		}

		[Obsolete("UnitTestOnly")]
		internal void SetRemoteLoaderUnitTestOnly(IRemoteServerService remoteLoader)
		{
			_remoteLoader = remoteLoader;
		}

		private void UploadBinaries(DirectoryInfo directory, string folder)
		{
			// all files in current folder
			var files = directory.GetFiles();
			foreach (var fileInfo in files)
			{
				// check if the file is needed for upload
				if (_shouldFileBeUploadedFunc(fileInfo))
				{
					byte[] hashCode = _fileHashCalculator.CalculateFastHashFromFile(fileInfo.FullName);
					using (var stream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
					{
						_remoteLoader.UploadFile(Path.Combine(folder, fileInfo.Name), hashCode, stream);
					}
				}
			}

			foreach (DirectoryInfo subfolder in directory.GetDirectories())
			{
				UploadBinaries(subfolder, Path.Combine(folder, subfolder.Name));
			}
		}

		T IRemoteLoader.CreateObject<T>(object[] parameters)
		{
			return _remotingClient.CreateRemoteInstance<T>(parameters);
		}

		T IRemoteLoader.CreateObject<T>()
		{
			return _remotingClient.CreateRemoteInstance<T>();
		}

		TReturn IRemoteLoaderClient.CreateObject<TCreate, TReturn>(object[] parameters)
		{
			return (TReturn)_remotingClient.CreateRemoteInstance(typeof(TCreate), parameters);
		}

		TReturn IRemoteLoaderClient.CreateObject<TCreate, TReturn>()
		{
			return _remotingClient.CreateRemoteInstance<TCreate>();
		}

		bool IRemoteLoader.IsSingleRemoteLoaderInstance()
		{
			// TODO: Implement
			return true;
		}

		public Process LaunchProcess(CancellationToken externalCancellation, bool isRemoteHostOnLocalMachine)
		{
			string arguments = _remoteLoaderId;
			IPAddress ip;

			// if we can determine the remote IP, we pass it to remoting server. The server will bind to this IP
			if (NetworkUtil.TryGetIpAddressForHostName(RemoteHost, out ip))
			{
				// If the target ip is loopback, try to use the external IP instead.
				// When we later forward this IP to our clients (i.e. user interface), it must be accessible from there as well. Telling a remote
				// client to use loopback to connect to the remote loader will not work.
				// Hint: If the local computer has more than one network interface, we would need to explicitly choose the one to use from outside
				// by setting the registry key.
				IPAddress localIp = NetworkUtil.GetLocalIp();
				if (isRemoteHostOnLocalMachine && ip.Equals(IPAddress.Loopback) && localIp != null)
				{
					ip = localIp;
					RemoteHost = ip.ToString();
				}

				arguments = string.Format(CultureInfo.InvariantCulture, "{0} {1}", arguments, ip);
			}

			return LaunchProcess(externalCancellation, isRemoteHostOnLocalMachine, REMOTELOADER_EXECUTABLE, REMOTELOADER_DEPENDENCIES_FILENAME, REMOTELOADER_DIRECTORY,
									arguments);
		}

		protected override bool WaitForRemoteProcessStartup(CancellationTokenSource linkedCancellationTokenSource, Process process)
		{
			// TODO: Implement FCMS-8056
			Thread.Sleep(1000);
			if (process.HasExited)
			{
				throw new IOException($"Process died during startup. Exit code {process.ExitCode}");
			}

			return true;
		}

		/// <exception cref="RemoteAccessException">Thrown if connection to remote loader fails</exception>
		public void Connect(CancellationToken externalToken)
		{
			Logger.LogInformation("Connecting RemoteLoader");

			var isRemoteHostOnLocalMachine = NetworkUtil.IsLocalIpAddress(RemoteHost);

			LaunchProcess(externalToken, isRemoteHostOnLocalMachine);

			_remotingClient = new Client(RemoteHost, Client.DefaultNetworkPort);
			_remoteLoader = _remotingClient.RequestRemoteInstance<IRemoteServerService>();
			Logger.LogInformation("Got interface to {0}", _remoteLoader.GetType().Name);
			if (_remoteLoader == null)
			{
				throw new RemoteAccessException("Could not connect to remote loader interface");
			}

			Stopwatch sw = Stopwatch.StartNew();
			// if the remote host is not a local machine we have to upload all necessary binaries and files
			if (!isRemoteHostOnLocalMachine)
			{
				UploadBinaries(LocalRootDirectory, string.Empty);
				_remoteLoader.UploadFinished();
			}

			Logger.LogInformation("BinaryUpload finished after '{0}'ms", sw.ElapsedMilliseconds);
		}

		protected override void Dispose(bool disposing)
		{
			if (_remotingClient != null)
			{
				_remotingClient.Dispose();
				_remotingClient = null;
			}

			base.Dispose(disposing);
		}
	}
}
