﻿namespace NewRemoting
{
	public interface IRemoteConsole
	{
		/// <summary>
		/// Directly launches a process on a remote machine.
		/// The process does not forward input, output or error stream, because the forwarding needs to be set up before the process is started.
		/// To use that functionality see <see cref="CreateProcess"/>.
		/// </summary>
		IProcess LaunchProcess(string commandLine, bool enableUserInterfaceInteraction = false);

		/// <summary>
		/// Creates a process on the remote machine which can be launched later on.
		/// Input, output and error streams can be redirected.
		/// </summary>
		IProcess CreateProcess(string commandLine, bool enableUserInterfaceInteraction = false,
			string fileListPath = null, string workingDirectory = null, bool redirectStandardOutput = false,
			bool redirectStandardError = false, bool redirectStandardInput = false, bool fireAndForget = false,
			bool useCsrc = false, string paexec_args = null);
	}
}
