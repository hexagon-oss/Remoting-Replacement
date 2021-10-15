using System;
using System.IO;

namespace NewRemoting
{
	public sealed class RemoteLoaderFactory : IRemoteLoaderFactory
	{
		public IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort = Client.DefaultNetworkPort)
		{
			return Create(remoteCredentials, remoteHost, remotePort, x => true);
		}

		public IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort, Func<FileInfo, bool> shouldFileBeUploadedFunc)
		{
			return Create(remoteCredentials, remoteHost, remotePort, new FileHashCalculator(), shouldFileBeUploadedFunc,
				TimeSpan.FromSeconds(1));
		}

		public IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort,
			FileHashCalculator fileHashCalculator,
			Func<FileInfo, bool> shouldFileBeUploadedFunc, TimeSpan waitTimeBetweenPaExecExecute)
		{
			if (OperatingSystem.IsWindows())
			{
				return new RemoteLoaderWindowsClient(remoteCredentials, remoteHost, remotePort,
					fileHashCalculator, shouldFileBeUploadedFunc, waitTimeBetweenPaExecExecute);
			}

			// TODO: Use SSH on linux
			throw new PlatformNotSupportedException(
				$"Remote process start is not implemented for {Environment.OSVersion.Platform}");
		}

	}
}
