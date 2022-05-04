using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Buffers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NewRemoting.Toolkit;

namespace NewRemoting
{
	public class RemoteLoaderWindowsClient : PaExecClient, IRemoteLoaderClient
	{
		public const string REMOTELOADER_EXECUTABLE = "RemotingServer.exe";
		private const string REMOTELOADER_DIRECTORY = @"%temp%\RemotingServer";
		private const string REMOTELOADER_DEPENDENCIES_FILENAME = REMOTELOADER_EXECUTABLE + ".dependencies.txt";
		// We pick a value that is the largest multiple of 4096 that is still smaller than the large object heap threshold (85K).
		private const int DEFAULT_COPY_BUFFER_SIZE = 81920;

		private readonly Func<FileInfo, bool> _shouldFileBeUploadedFunc;
		private readonly FileHashCalculator _fileHashCalculator;
		private readonly string _extraArguments;

		private IRemoteServerService _remoteServer;
		private Client _remotingClient;

		public RemoteLoaderWindowsClient(Credentials remoteCredentials, string remoteHost, int remotePort,
			FileHashCalculator fileHashCalculator,
			Func<FileInfo, bool> shouldFileBeUploadedFunc, TimeSpan waitTimeBetweenPaExecExecute, string extraArguments, ILogger logger = null)
			: base(remoteCredentials, remoteHost, waitTimeBetweenPaExecExecute, logger)
		{
			RemotePort = remotePort;
			_extraArguments = extraArguments;
			_shouldFileBeUploadedFunc = shouldFileBeUploadedFunc ?? throw new ArgumentNullException(nameof(shouldFileBeUploadedFunc));
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

		public int RemotePort
		{
			get;
		}

		/// <summary>
		/// Gets the internal remote client reference.
		/// May be required for advanced service queries.
		/// </summary>
		public Client RemoteClient => _remotingClient;

		private void UploadBinaries(DirectoryInfo directory, string folder)
		{
			// all files in current folder
			var files = directory.GetFiles();
			foreach (var fileInfo in files)
			{
				// check if the file is needed for upload
				if (_shouldFileBeUploadedFunc(fileInfo))
				{
					UploadFile(folder, fileInfo);
				}
			}

			foreach (DirectoryInfo subfolder in directory.GetDirectories())
			{
				UploadBinaries(subfolder, Path.Combine(folder, subfolder.Name));
			}
		}

		private void UploadFile(string folder, FileInfo file)
		{
			byte[] hashCode = _fileHashCalculator.CalculateFastHashFromFile(file.FullName);
			bool uploadFile = _remoteServer.PrepareFileUpload(Path.Combine(folder, file.Name), hashCode);

			if (uploadFile)
			{
				using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					int fileLengthInt = (file.Length > int.MaxValue) ? int.MaxValue : (int)file.Length;
					int bufferSize = Math.Min(DEFAULT_COPY_BUFFER_SIZE, fileLengthInt);
					byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
					int read;

					while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
					{
						_remoteServer.UploadFileData(buffer, read);
					}

					ArrayPool<byte>.Shared.Return(buffer);
				}

				_remoteServer.FinishFileUpload();
			}
		}

		public T CreateObject<T>(object[] parameters)
			where T : MarshalByRefObject
		{
			return _remotingClient.CreateRemoteInstance<T>(parameters);
		}

		public T CreateObject<T>()
			where T : MarshalByRefObject
		{
			return _remotingClient.CreateRemoteInstance<T>();
		}

		public TReturn CreateObject<TCreate, TReturn>(object[] parameters)
			where TCreate : MarshalByRefObject, TReturn
			where TReturn : class
		{
			return (TReturn)_remotingClient.CreateRemoteInstance(typeof(TCreate), parameters);
		}

		public TReturn CreateObject<TCreate, TReturn>()
			where TCreate : MarshalByRefObject, TReturn
			where TReturn : class
		{
			return _remotingClient.CreateRemoteInstance<TCreate>();
		}

		public T RequestRemoteInstance<T>()
			where T : class
		{
			return _remotingClient.RequestRemoteInstance<T>();
		}

		public IProcess LaunchProcess(CancellationToken externalCancellation, bool isRemoteHostOnLocalMachine)
		{
			return LaunchProcess(externalCancellation, isRemoteHostOnLocalMachine, REMOTELOADER_EXECUTABLE, _extraArguments, REMOTELOADER_DEPENDENCIES_FILENAME, REMOTELOADER_DIRECTORY);
		}

		protected override bool WaitForRemoteProcessStartup(CancellationTokenSource linkedCancellationTokenSource, IProcess process)
		{
			// TODO: Implement FCMS-8056
			Thread.Sleep(1000);
			if (process.HasExited)
			{
				string errorMsg = $"Remoting process died during startup. Exit code {process.ExitCode}";
				Logger.LogError(errorMsg);
				throw new RemotingException(errorMsg);
			}

			return true;
		}

		/// <inheritdoc />
		public void Connect(CancellationToken externalToken, ILogger clientConnectionLogger)
		{
			Logger.LogInformation("Connecting to RemotingServer");

			var isRemoteHostOnLocalMachine = NetworkUtil.IsLocalIpAddress(RemoteHost);

			LaunchProcess(externalToken, isRemoteHostOnLocalMachine);

			_remotingClient = null;

			Exception lastError = null;

			for (int retries = 0; retries < 5; retries++)
			{
				try
				{
					lastError = null;
					Logger.LogInformation("Creating remoting client");
					_remotingClient = new Client(RemoteHost, RemotePort, Credentials.Certificate,
						clientConnectionLogger, Logger);
					Logger.LogInformation("Remoting client creation succeeded");
					break;
				}
				catch (Exception x) when (x is IOException || x is SocketException || x is UnauthorizedAccessException)
				{
					Logger.LogError(x, $"Unable to connect to remote server. Attempt {retries + 1}: {x.Message}");
					lastError = x;
				}
				catch (Exception x)
				{
					Logger.LogError(x, $"Unable to connect to remote server. Attempt {retries + 1}: {x.Message}, aborting");
					throw;
				}

				Thread.Sleep(1000); // Maybe the server hasn't started properly yet
			}

			if (lastError != null)
			{
				Logger.LogError($"Unable to connect to remote server, aborting");
				throw lastError;
			}

			_remoteServer = _remotingClient.RequestRemoteInstance<IRemoteServerService>();
			Logger.LogInformation("Got interface to {0}", _remoteServer.GetType().Name);
			if (_remoteServer == null)
			{
				throw new RemotingException("Could not connect to remote loader interface");
			}

			Stopwatch sw = Stopwatch.StartNew();
			// if the remote host is not a local machine we have to upload all necessary binaries and files
			if (!isRemoteHostOnLocalMachine)
			{
				UploadBinaries(LocalRootDirectory, string.Empty);
				_remoteServer.UploadFinished();
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
