using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NewRemoting.Toolkit;

namespace NewRemoting
{
	public class PaExecClient : IDisposable
	{
		/// <summary>
		/// Return code from PAEXEC
		/// </summary>
		internal const int PAEXEC_SERVICE_COULD_NOT_BE_INSTALLED = -6;
		internal const int PAEXEC_FAILED_TO_COPY_APP = -8;

		private static readonly TimeSpan DefaultTerminationTimeout = TimeSpan.FromSeconds(10);

		/// <summary>
		/// Protect <see cref="_internalCancellationTokenSource"/>, since the call to <see cref="IDisposable.Dispose"/>
		/// and the <see cref="Process.Exited"/> event handler can be called
		/// simultaneously and a disposed token source be used in the event handler.
		/// Use field initializer to prevent a <see cref="NullReferenceException"/> in destructor when constructor has thrown.
		/// </summary>
		private readonly object _internalCancellationTokenSourceLock = new object();

		private Credentials _remoteCredentials;
		private TimeSpan _waitTimeBetweenPaExecExecute;
		private DirectoryInfo _root;
		private ILogger _logger;

		private ResetableCancellationTokenSource _internalCancellationTokenSource;
		private Process _process;
		private string _remoteHost;

		public PaExecClient(Credentials remoteCredentials, string remoteHost)
			: this(remoteCredentials, remoteHost, TimeSpan.FromSeconds(1))
		{
		}

		/// <exception cref="ArgumentNullException">Thrown if credentials are null</exception>
		/// <exception cref="ArgumentException">Thrown if host name is invlalid</exception>
		public PaExecClient(Credentials remoteCredentials, string remoteHost, TimeSpan waitTimeBetweenPaExecExecute, ILogger logger = null)
		{
			if (remoteCredentials == null)
			{
				throw new ArgumentNullException(nameof(remoteCredentials));
			}

			if (string.IsNullOrWhiteSpace(remoteHost))
			{
				throw new ArgumentException("remote host must not be null or whitespace", nameof(remoteHost));
			}

			_waitTimeBetweenPaExecExecute = waitTimeBetweenPaExecExecute;

			_root = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
			_remoteHost = remoteHost;
			_remoteCredentials = remoteCredentials;

			_internalCancellationTokenSource = new ResetableCancellationTokenSource();

			_logger = logger ?? NullLogger.Instance;
		}

		~PaExecClient()
		{
			Dispose(false);
		}

		/// <summary>
		/// Output data received from process. LogLevel is either Information (for normal output) or Error (for output on the error pipe)
		/// </summary>
		public event Action<string, LogLevel> OutputDataReceived;

		protected ILogger Logger => _logger;

		public string RemoteHost
		{
			get
			{
				return _remoteHost;
			}

			protected set
			{
				_remoteHost = value;
			}
		}

		protected DirectoryInfo LocalRootDirectory => _root;

		protected virtual void Dispose(bool disposing)
		{
			if (_process != null)
			{
				WaitForRemoteProcessTermination(DefaultTerminationTimeout, true);
				_logger.LogInformation("Process Exit code: {0}", _process.ExitCode);
				_process.Dispose();
				_process = null;
			}

			if (disposing)
			{
				lock (_internalCancellationTokenSourceLock)
				{
					if (_internalCancellationTokenSource != null)
					{
						_internalCancellationTokenSource.Dispose();
						_internalCancellationTokenSource = null;
					}
				}
			}
		}

		public virtual void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		internal static Process CreateProcessLocal(string executableName, string arguments)
		{
			var startInfo = new ProcessStartInfo
			{
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = false,
				Arguments = arguments,
				FileName = executableName
			};
			var unstartedProcess = new Process();
			unstartedProcess.StartInfo = startInfo;
			return unstartedProcess;
		}

		protected static bool IsPaexecRetryableErrorCode(int errorCode)
		{
			return errorCode == RemoteLoaderClient.PAEXEC_SERVICE_COULD_NOT_BE_INSTALLED || errorCode == RemoteLoaderClient.PAEXEC_FAILED_TO_COPY_APP;
		}

		private static string PrependAppDomainPath(string file)
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
		}

		private Process CreateProcess(string commandLine, bool enableUserInterfaceInteraction = false, string fileListPath = null, string workingDirectory = null, bool redirectStandardOutput = false, bool redirectStandardError = false, bool redirectStandardInput = false)
		{
			var startInfo = new ProcessStartInfo();
			startInfo.CreateNoWindow = false;
			startInfo.WindowStyle = ProcessWindowStyle.Normal;
			startInfo.UseShellExecute = false;
			startInfo.RedirectStandardOutput = redirectStandardOutput;
			startInfo.RedirectStandardError = redirectStandardError;
			startInfo.RedirectStandardInput = redirectStandardInput;
			startInfo.FileName = PrependAppDomainPath(RemoteConsole.PAEXEC_EXECUTABLE);
			// -dfr parameter is needed to disable WOW64 File Redirection for the new process
			var interactionArgument = enableUserInterfaceInteraction ? "-i " : string.Empty;
			var copyArgments = string.IsNullOrEmpty(fileListPath) ? string.Empty : FormattableString.Invariant($"-c -f -clist {fileListPath} ");
			var workingDirAgruments = string.IsNullOrEmpty(workingDirectory) ? string.Empty : FormattableString.Invariant($"-w \"{workingDirectory}\" ");
			startInfo.Arguments = FormattableString.Invariant($@"\\{_remoteHost} -u {_remoteCredentials.DomainQualifiedUsername} -p {_remoteCredentials.Password} -dfr {interactionArgument}{workingDirAgruments}{copyArgments}{commandLine}");

			var unstartedProcess = new Process();
			unstartedProcess.StartInfo = startInfo;
			return unstartedProcess;
		}

		/// <summary>
		/// Launches the remote loader executable on the remote system, therefor:
		/// - all dependencies will be uploaded %TEMP% folder of remote system
		/// - %TEMP% folder path will be read from remote system, path is dynamic so we have to read it first
		/// </summary>
		/// <param name="externalCancellation">Token to abort process creation</param>
		/// <param name="processName">Name of executable to start</param>
		/// <param name="dependenciesFile">Name of a file containing dependencies to upload (or null)</param>
		/// <param name="remoteFileDirectory">Temp directory where the executable should be copied to. Will be created if doesn't exist yet</param>
		/// <param name="arguments">Arguments to remote program</param>
		/// <returns>A process instance, pointing to the remote process</returns>
		/// <exception cref="RemoteAccessException">Error sharing local folder</exception>
		protected virtual Process CreateProcessOnRemoteMachine(CancellationToken externalCancellation, string processName, string dependenciesFile, string remoteFileDirectory, string arguments)
		{
			var remoteConsole = new RemoteConsole(_remoteHost, _remoteCredentials);
			// 1. Because not all systems use the same folder for %TEMP% we read the path from remote system.
			// 2. Create working directory if not exist on remote system
			string workingDirectory = string.Empty;
			int exitCode = -1;

			// We try to run the process max 3 times. On older systems it is possible that ping is success but
			// paecec failes to run!
			var proc = remoteConsole.CreateProcess(FormattableString.Invariant($"c:\\windows\\system32\\cmd.exe /c echo {remoteFileDirectory} & if not exist \"{remoteFileDirectory}\" mkdir \"{remoteFileDirectory}\""), redirectStandardOutput: true);
			int runCounter = 1;
			while (!externalCancellation.IsCancellationRequested)
			{
				_logger.LogInformation("Try to read/create remote loader subdirectory from/on remote system -> Count: " + runCounter++);
				proc.Start();

				workingDirectory = proc.StandardOutput.ReadToEnd().Trim('\r', '\n', ' ');
				proc.WaitForExit();

				exitCode = proc.ExitCode;

				if (IsPaexecRetryableErrorCode(exitCode))
				{
					externalCancellation.WaitHandle.WaitOne(_waitTimeBetweenPaExecExecute);
				}
				else if (exitCode != 0 && !IsPaexecRetryableErrorCode(exitCode))
				{
					// break immediately on other error codes than -6, -8 and 0
					break;
				}

				if (exitCode == 0 && !string.IsNullOrEmpty(workingDirectory))
				{
					break;
				}
			}

			if (exitCode != 0 || string.IsNullOrEmpty(workingDirectory))
			{
				throw new RemoteAccessException(FormattableString.Invariant($"Could not create or read working directory. ErrorCode: {exitCode}, WorkingDir: '{workingDirectory}'"));
			}

			// Launch remote loader
			var commandLaunch = FormattableString.Invariant($"\"{Path.Combine(workingDirectory, processName)}\" {arguments}");
			_logger.LogInformation("Execute command {0} on {1}", commandLaunch, _remoteHost);
			return remoteConsole.CreateProcess(commandLaunch, false, dependenciesFile, workingDirectory, true, true, false);
		}

		/// <summary>
		/// Launches a process on the remote machine. Returns the process handle of the running instance.
		/// </summary>
		/// <param name="externalToken">Cancellation token (does only cancel the startup attempt, not the process itself, if launching was successful</param>
		/// <param name="isRemoteHostOnLocalMachine">True if the process should actually be started locally. Null to auto-detect</param>
		/// <param name="processName">Name of the remote process</param>
		/// <param name="dependenciesFile">A file containing the set of files to copy to the remote machine before execution</param>
		/// <param name="remoteFileDirectory">The directory on the remote machine where the file should be copied to</param>
		/// <param name="arguments">Arguments to the remote process</param>
		/// <returns>The created process. Do NOT dispose the returned process instance directly, but dispose the <see cref="PaExecClient"/> instance instead.</returns>
		public virtual Process LaunchProcess(CancellationToken externalToken, bool? isRemoteHostOnLocalMachine, string processName,
			string dependenciesFile, string remoteFileDirectory, string arguments)
		{
			var sw = Stopwatch.StartNew();
			if (!isRemoteHostOnLocalMachine.HasValue)
			{
				isRemoteHostOnLocalMachine = NetworkUtil.IsLocalIpAddress(RemoteHost);
			}

			if (!PingUtil.CancellableTryWaitForPingResponse(_remoteHost, TimeSpan.MaxValue, externalToken))
			{
				_process = null;
				return _process;
			}

			// try to launch the remote process.. retry until canceled
			while (_process == null)
			{
				var process = isRemoteHostOnLocalMachine.Value ?
					CreateProcessLocal(PrependAppDomainPath(processName), arguments) :
					CreateProcessOnRemoteMachine(externalToken, processName, dependenciesFile, remoteFileDirectory, arguments);
				_logger.LogInformation("Process created after '{0}'ms", sw.ElapsedMilliseconds);
				// Cancel operation if process exits or canceled externally
				lock (_internalCancellationTokenSourceLock)
				{
					_internalCancellationTokenSource.Reset();
				}

				using (var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _internalCancellationTokenSource.Token))
				{
					// Setup events on process before starting it
					process.EnableRaisingEvents = true;
					process.OutputDataReceived += (sender, args) =>
					{
						if (!string.IsNullOrEmpty(args.Data))
						{
							if (OutputDataReceived != null)
							{
								OutputDataReceived.Invoke(args.Data, LogLevel.Information);
							}
						}
					};
					process.ErrorDataReceived += (sender, args) =>
					{
						if (!string.IsNullOrEmpty(args.Data))
						{
							if (OutputDataReceived != null)
							{
								OutputDataReceived.Invoke(args.Data, LogLevel.Error);
							}
						}
					};
					process.Exited += (sender, args) =>
					{
						lock (_internalCancellationTokenSourceLock)
						{
							_internalCancellationTokenSource?.Cancel();
						}
					};
					Logger.LogInformation(FormattableString.Invariant($"Starting remote process: {process.StartInfo.FileName} in {process.StartInfo.WorkingDirectory} with {process.StartInfo.Arguments}"));
					process.Start();
					process.BeginOutputReadLine();
					process.BeginErrorReadLine();
					Logger.LogInformation("Process started after '{0}'ms", sw.ElapsedMilliseconds);
					if (WaitForRemoteProcessStartup(linkedCancellationTokenSource, process))
					{
						_process = process; // remote server is running and remote interface is available --> exit the loop
						break;
					}

					try
					{
						// throw canceled exception if it was external canceled
						externalToken.ThrowIfCancellationRequested();

						// process have to be terminated here, because operation is only canceled externally or if process exits
						if (!process.HasExited)
						{
							var errorMsg = string.Format(CultureInfo.InvariantCulture, "Could not get interface of remote loader on machine {0} ", _remoteHost);
							Logger.LogError(errorMsg);

							process.Kill(); // if process is still running, we didn't receive the process exit event - this should never happen but just in case we terminate the remote process
							throw new RemoteAccessException(errorMsg);
						}

						int errorCode = process.ExitCode;
						if (IsPaexecRetryableErrorCode(errorCode)) // this can happen after ping was successfully but service manager is not yet running
						{
							Logger.LogInformation(FormattableString.Invariant($"Paexec service cound not be installed, error code {errorCode} - retrying"));
						}
						else
						{
							var errorMsg = string.Format(CultureInfo.InvariantCulture, "Could not launch remote loader on machine {0} Error Code: {1} Arguments {2}", _remoteHost, errorCode, arguments);
							Logger.LogError(errorMsg);
							throw new RemoteAccessException(errorMsg);
						}
					}
					finally
					{
						process.Dispose();
					}
				}
			}

			return _process;
		}

		public virtual bool WaitForRemoteProcessTermination(TimeSpan timeout, bool killAfterTimeout)
		{
			if (_process.HasExited)
			{
				return true;
			}

			if (_process.WaitForExit((int)timeout.TotalMilliseconds))
			{
				return true;
			}

			if (killAfterTimeout)
			{
				_process.Kill();
				return true;
			}

			return false;
		}

		protected virtual bool WaitForRemoteProcessStartup(CancellationTokenSource linkedCancellationTokenSource, Process process)
		{
			Thread.Sleep(100);
			return true;
		}
	}
}
