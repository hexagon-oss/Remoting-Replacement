using System;
using System.Diagnostics;
using System.IO;
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
		private readonly TimeSpan _remoteProcessConnectionTimeout = TimeSpan.FromSeconds(120);

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

		/// <summary>
		/// The remote network port in use
		/// </summary>
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

		public IProcess LaunchProcess(CancellationToken externalCancellation, bool isRemoteHostOnLocalMachine, ILogger clientConnectionLogger)
		{
			var workingDir = isRemoteHostOnLocalMachine ? AppDomain.CurrentDomain.BaseDirectory : REMOTELOADER_DIRECTORY;
			return LaunchProcess(externalCancellation, isRemoteHostOnLocalMachine, REMOTELOADER_EXECUTABLE, _extraArguments, REMOTELOADER_DEPENDENCIES_FILENAME, workingDir, clientConnectionLogger: clientConnectionLogger);
		}

		protected override bool WaitForRemoteProcessStartup(CancellationTokenSource linkedCancellationTokenSource, IProcess process)
		{
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

			var process = LaunchProcess(externalToken, isRemoteHostOnLocalMachine, Logger);

			_remotingClient = null;

			Exception lastError = null;

			for (int retries = 0; retries < 5; retries++)
			{
				try
				{
					lastError = null;
					Logger.LogInformation("Creating remoting client");
					ConnectionSettings settings = new ConnectionSettings()
					{
						InstanceManagerLogger = Logger,
						ConnectionLogger = clientConnectionLogger,
					};

					_remotingClient = new Client(RemoteHost, RemotePort, Credentials.Certificate, settings);
					Logger.LogInformation("Remoting client creation succeeded");
					break;
				}
				catch (Exception x) when (x is IOException || x is SocketException || x is UnauthorizedAccessException)
				{
					Logger.LogError(x, $"Unable to connect to remote server. Attempt {retries + 1}: {x.Message}");
					lastError = x;

					if (process is { HasExited: true, ExitCode: (int)ExitCode.StartFailure })
					{
						var ex = new RemotingException(message: $"Process exited with exit code {(int)ExitCode.StartFailure}.")
						{
							AdditionalInfo = RemotingExceptionAdditionalInfo.ProcessStartFailure
						};
						lastError = ex;
						break;
					}
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

			string verifyResult = _remotingClient.VerifyMatchingServer();
			if (!string.IsNullOrWhiteSpace(verifyResult))
			{
				throw new RemotingException(verifyResult);
			}

			Logger.LogInformation("BinaryUpload start");
			Stopwatch sw = Stopwatch.StartNew();
			// if the remote host is not a local machine we have to upload all necessary binaries and files
			if (!isRemoteHostOnLocalMachine)
			{
				UploadBinaries(LocalRootDirectory, string.Empty);
				_remoteServer.UploadFinished();
			}

			Logger.LogInformation("BinaryUpload finished after '{0}'ms", sw.ElapsedMilliseconds);
		}

		public bool Connect(bool checkExistingInstance, CancellationToken cancellationToken, ILogger clientConnectionLogger = null)
		{
			clientConnectionLogger?.LogInformation($"Connection sequence for {REMOTELOADER_EXECUTABLE} started.");

			if (checkExistingInstance)
			{
				var isRemoteHostOnLocalMachine = NetworkUtil.IsLocalIpAddress(RemoteHost);
				if (isRemoteHostOnLocalMachine)
				{
					// should not throw on windows plaform (if no machine name is used)
					Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(REMOTELOADER_EXECUTABLE));
					if (processes.Length > 0)
					{
						clientConnectionLogger?.LogInformation($"{REMOTELOADER_EXECUTABLE} process already exist.");
						return false;
					}
				}
				else
				{
					clientConnectionLogger?.LogInformation($"checking if {REMOTELOADER_EXECUTABLE} process already exist.");
					var workingDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
					var powershell = "powershell";
					// if the process count is greater than 0
					var args = $"@(get-process -Name {Path.GetFileNameWithoutExtension(REMOTELOADER_EXECUTABLE)}).Count -gt 0";
					var remoteConsole = new RemoteConsole(RemoteHost, Credentials);

					using (CancellationTokenSource timeoutCts = new CancellationTokenSource(_remoteProcessConnectionTimeout))
					{
						CancellationTokenSource combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

						using (var proc = remoteConsole.CreateProcess(powershell + " " + args, enableUserInterfaceInteraction: false, fileListPath: null, workingDirectory: workingDir, redirectStandardOutput: true, redirectStandardError: true))
						{
							clientConnectionLogger?.LogInformation("powershell command about to start.");
							proc.Start();
							while (!proc.HasExited && !combinedCancellation.IsCancellationRequested)
							{
								clientConnectionLogger?.LogInformation("waiting for powershell command process to exit.");
								proc.WaitForExit(100);
							}

							if (combinedCancellation.IsCancellationRequested)
							{
								clientConnectionLogger?.LogError($"Timeout waiting starting remote process {REMOTELOADER_EXECUTABLE}.");
								throw new OperationCanceledException($"Timeout waiting starting remote process {REMOTELOADER_EXECUTABLE}.");
							}

							var err = proc.StandardError.ReadToEnd();

							if (!string.IsNullOrEmpty(err))
							{
								clientConnectionLogger?.LogWarning($"Powershell standard error output {err}.");
							}

							if (proc.HasExited && proc.ExitCode == 0)
							{
								var output = proc.StandardOutput.ReadToEnd();
								if (output.StartsWith("True"))
								{
									clientConnectionLogger?.LogWarning($"{REMOTELOADER_EXECUTABLE} process already exist on the remote machine.");
									return false;
								}
							}
							else
							{
								throw new RemotingException("Unable to run command line on remoting machine. Possible reason : remote machine does not exist.");
							}
						}
					}
				}
			}

			clientConnectionLogger?.LogInformation("Starting Connection to remote server.");
			Connect(cancellationToken, clientConnectionLogger);
			return true;
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
