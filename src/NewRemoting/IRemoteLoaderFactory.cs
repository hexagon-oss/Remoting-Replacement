using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace NewRemoting
{
	/// <summary>
	/// Factory to create remote loaders.
	/// </summary>
	public interface IRemoteLoaderFactory
	{
		/// <summary>
		/// Create a remoting server anywhere
		/// </summary>
		/// <param name="remoteCredentials">Credentials for connecting to the remote system</param>
		/// <param name="remoteHost">Host name to connect to. If this is localhost, the server will be created on the same machine</param>
		/// <param name="remotePort">Network port to use</param>
		/// <param name="startupLogger">A logger for logging startup errors</param>
		/// <returns>An <see cref="IRemoteLoaderClient"/> instance that can be used to connect to the server process</returns>
		IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort = Client.DefaultNetworkPort, ILogger startupLogger = null);

		/// <summary>
		/// Create a remoting server anywhere
		/// </summary>
		/// <param name="remoteCredentials">Credentials for connecting to the remote system</param>
		/// <param name="remoteHost">Host name to connect to. If this is localhost, the server will be created on the same machine</param>
		/// <param name="remotePort">Network port to use</param>
		/// <param name="shouldFileBeUploadedFunc">A function that determines whether the given file should be copied to the remote end during startup.</param>
		/// <param name="startupLogger">A logger for logging startup errors</param>
		/// <returns>An <see cref="IRemoteLoaderClient"/> instance that can be used to connect to the server process</returns>
		IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort, Func<FileInfo, bool> shouldFileBeUploadedFunc, ILogger startupLogger = null);

		/// <summary>
		/// Create a remoting server anywhere
		/// </summary>
		/// <param name="remoteCredentials">Credentials for connecting to the remote system</param>
		/// <param name="remoteHost">Host name to connect to. If this is localhost, the server will be created on the same machine</param>
		/// <param name="remotePort">Network port to use</param>
		/// <param name="fileHashCalculator">A consistent hash calculation function</param>
		/// <param name="shouldFileBeUploadedFunc">A function that determines whether the given file should be copied to the remote end during startup.</param>
		/// <param name="waitTimeBetweenPaExecExecute">Time between connection attempts</param>
		/// <param name="extraArguments">Extra arguments to provide to the remote server</param>
		/// <param name="startupLogger">A logger for logging startup errors</param>
		/// <returns>An <see cref="IRemoteLoaderClient"/> instance that can be used to connect to the server process</returns>
		IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort,
			FileHashCalculator fileHashCalculator,
			Func<FileInfo, bool> shouldFileBeUploadedFunc, TimeSpan waitTimeBetweenPaExecExecute, string extraArguments, ILogger startupLogger = null);
	}
}
