using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace NewRemoting
{
	public sealed class RemoteLoaderFactory : IRemoteLoaderFactory
	{
		public IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort = Client.DefaultNetworkPort, ILogger logger = null)
		{
			return Create(remoteCredentials, remoteHost, remotePort, x => true, logger);
		}

		public IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort, Func<FileInfo, bool> shouldFileBeUploadedFunc, ILogger logger = null)
		{
			return Create(remoteCredentials, remoteHost, remotePort, new FileHashCalculator(), shouldFileBeUploadedFunc,
				TimeSpan.FromSeconds(1), logger);
		}

		public IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort,
			FileHashCalculator fileHashCalculator,
			Func<FileInfo, bool> shouldFileBeUploadedFunc, TimeSpan waitTimeBetweenPaExecExecute, ILogger logger = null)
		{
			if (OperatingSystem.IsWindows())
			{
				return new RemoteLoaderWindowsClient(remoteCredentials, remoteHost, remotePort,
					fileHashCalculator, shouldFileBeUploadedFunc, waitTimeBetweenPaExecExecute, logger);
			}

			// TODO: Use SSH on linux
			throw new PlatformNotSupportedException(
				$"Remote process start is not implemented for {Environment.OSVersion.Platform}");
		}

	}
}
