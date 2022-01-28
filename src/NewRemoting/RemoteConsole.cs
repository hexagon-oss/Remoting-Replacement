using System;
using System.Diagnostics;

namespace NewRemoting
{
	internal sealed class RemoteConsole : IRemoteConsole
	{
		public const string PAEXEC_EXECUTABLE = "paexec.exe";

		private readonly Credentials _remoteCredentials;
		private readonly string _remoteHost;

		/// <exception cref="ArgumentNullException">Thrown if argument was null</exception>
		/// <exception cref="ArgumentException">Thrown if argument was invalid</exception>
		public RemoteConsole(string remoteHost, Credentials remoteCredentials)
		{
			if (remoteCredentials == null)
			{
				throw new ArgumentNullException(nameof(remoteCredentials));
			}

			if (string.IsNullOrWhiteSpace(remoteHost))
			{
				throw new ArgumentException("remote host is invalid", nameof(remoteCredentials));
			}

			_remoteCredentials = remoteCredentials;
			_remoteHost = remoteHost;
		}

		public Process CreateProcess(string commandLine, bool enableUserInterfaceInteraction = false, string fileListPath = null, string workingDirectory = null, bool redirectStandardOutput = false, bool redirectStandardError = false, bool redirectStandardInput = false)
		{
			var startInfo = new ProcessStartInfo();
			startInfo.CreateNoWindow = true;
			startInfo.WindowStyle = ProcessWindowStyle.Hidden;
			startInfo.UseShellExecute = false;
			startInfo.RedirectStandardOutput = redirectStandardOutput;
			startInfo.RedirectStandardError = redirectStandardError;
			startInfo.RedirectStandardInput = redirectStandardInput;
			startInfo.FileName = PAEXEC_EXECUTABLE;
			// -dfr parameter is needed to disable WOW64 File Redirection for the new process
			var interactionArgument = enableUserInterfaceInteraction ? "-i " : string.Empty;
			var copyArgments = string.IsNullOrEmpty(fileListPath) ? string.Empty : FormattableString.Invariant($"-c -f -clist {fileListPath} ");
			var workingDirAgruments = string.IsNullOrEmpty(workingDirectory) ? string.Empty : FormattableString.Invariant($"-w \"{workingDirectory}\" ");
			startInfo.Arguments = FormattableString.Invariant($@"\\{_remoteHost} -u {_remoteCredentials.DomainQualifiedUsername} -p {_remoteCredentials.Password} -dfr -cnodel {interactionArgument}{workingDirAgruments}{copyArgments}{commandLine}");

			var unstartedProcess = new Process();
			unstartedProcess.StartInfo = startInfo;
			return unstartedProcess;
		}

		public Process LaunchProcess(string commandLine, bool enableUserInterfaceInteraction = false)
		{
			var process = CreateProcess(commandLine, enableUserInterfaceInteraction, null, null, false, false, false);
			process.Start();
			return process;
		}
	}
}
