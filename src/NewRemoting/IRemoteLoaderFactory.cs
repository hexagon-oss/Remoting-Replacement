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
		IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort = Client.DefaultNetworkPort, ILogger logger = null);
		IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort, Func<FileInfo, bool> shouldFileBeUploadedFunc, ILogger logger = null);

		IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort,
			FileHashCalculator fileHashCalculator,
			Func<FileInfo, bool> shouldFileBeUploadedFunc, TimeSpan waitTimeBetweenPaExecExecute, ILogger logger = null);
	}
}
