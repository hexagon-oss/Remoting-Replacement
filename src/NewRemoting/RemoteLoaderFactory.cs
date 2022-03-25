using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace NewRemoting
{
	public sealed class RemoteLoaderFactory : IRemoteLoaderFactory
	{
		public IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort = Client.DefaultNetworkPort, ILogger startupLogger = null)
		{
			return Create(remoteCredentials, remoteHost, remotePort, x => true, startupLogger);
		}

		public IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort, Func<FileInfo, bool> shouldFileBeUploadedFunc, ILogger startupLogger = null)
		{
			return Create(remoteCredentials, remoteHost, remotePort, new FileHashCalculator(), shouldFileBeUploadedFunc,
				TimeSpan.FromSeconds(1), string.Empty, startupLogger);
		}

		public IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort,
			FileHashCalculator fileHashCalculator, Func<FileInfo, bool> shouldFileBeUploadedFunc,
			TimeSpan waitTimeBetweenPaExecExecute, string extraArguments, ILogger startupLogger = null)
		{
			if (OperatingSystem.IsWindows())
			{
				return new RemoteLoaderWindowsClient(remoteCredentials, remoteHost, remotePort,
					fileHashCalculator, shouldFileBeUploadedFunc, waitTimeBetweenPaExecExecute, extraArguments, startupLogger);
			}

			// TODO: Use SSH on linux
			throw new PlatformNotSupportedException(
				$"Remote process start is not implemented for {Environment.OSVersion.Platform}");
		}

	}
}
