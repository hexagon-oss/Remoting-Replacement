using System;
using System.Diagnostics;
using System.IO;
using Castle.Core.Internal;

namespace NewRemoting
{
	internal sealed class RemoteConsole : IRemoteConsole
	{
		public const string PAEXEC_EXECUTABLE = "paexec.exe";

		private readonly Credentials _remoteCredentials;
		private readonly string _remoteHost;
		private readonly IProcessWrapperFactory _processWrapperFactory;

		/// <exception cref="ArgumentNullException">Thrown if argument was null</exception>
		/// <exception cref="ArgumentException">Thrown if argument was invalid</exception>
		public RemoteConsole(string remoteHost, Credentials remoteCredentials)
			: this(remoteHost, remoteCredentials, new ProcessWrapperFactory())
		{
		}

		internal RemoteConsole(string remoteHost, Credentials remoteCredentials,
			IProcessWrapperFactory processWrapperFactory)
		{
			if (string.IsNullOrWhiteSpace(remoteHost))
			{
				throw new ArgumentException("remote host is invalid", nameof(remoteCredentials));
			}

			_processWrapperFactory = processWrapperFactory ?? throw new ArgumentNullException(nameof(processWrapperFactory));
			_remoteCredentials = remoteCredentials ?? throw new ArgumentNullException(nameof(remoteCredentials));
			_remoteHost = remoteHost;
		}

		public IProcess CreateProcess(string commandLine, bool enableUserInterfaceInteraction = false, string fileListPath = null, string workingDirectory = null, bool redirectStandardOutput = false, bool redirectStandardError = false, bool redirectStandardInput = false, bool fireAndForget = false, bool useCsrc = false, string moreArgs = null)
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
			var workingDirAgruments = string.IsNullOrEmpty(workingDirectory) ? string.Empty : FormattableString.Invariant($"-w \"{workingDirectory}\" ");
			string additionalArgs = string.Empty;
			if (!string.IsNullOrEmpty(moreArgs))
			{
				additionalArgs = moreArgs;
			}

			var fireAndForgetArgument = fireAndForget ? "-d -cnodel " : string.Empty;
			string copyArguments;
			string commandLineToUse = commandLine;
			if (string.IsNullOrEmpty(fileListPath))
			{
				if (useCsrc)
				{
					commandLineToUse = Path.GetFileName(commandLine);
					copyArguments = FormattableString.Invariant($"-csrc {commandLine} -c ");
				}
				else
				{
					copyArguments = string.Empty;
				}
			}
			else
			{
				copyArguments = FormattableString.Invariant($"-f -clist {fileListPath} -c ");
			}

			startInfo.Arguments = FormattableString.Invariant($@"\\{_remoteHost} -u {_remoteCredentials.DomainQualifiedUsername} -p {_remoteCredentials.Password} {fireAndForgetArgument}-dfr {interactionArgument}{workingDirAgruments}{copyArguments}{additionalArgs}{commandLineToUse}");

			var unstartedProcess = _processWrapperFactory.CreateProcess();
			unstartedProcess.StartInfo = startInfo;
			return unstartedProcess;
		}

		public IProcess LaunchProcess(string commandLine, bool enableUserInterfaceInteraction = false)
		{
			var process = CreateProcess(commandLine, enableUserInterfaceInteraction, null, null, false, false, false);
			process.Start();
			return process;
		}
	}
}
