using System;
using System.IO;

namespace NewRemoting
{
	/// <summary>
	/// Factory to create remote loaders.
	/// </summary>
	public interface IRemoteLoaderFactory
	{
		IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort = Client.DefaultNetworkPort);
		IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort, Func<FileInfo, bool> shouldFileBeUploadedFunc);

		IRemoteLoaderClient Create(Credentials remoteCredentials, string remoteHost, int remotePort,
			FileHashCalculator fileHashCalculator,
			Func<FileInfo, bool> shouldFileBeUploadedFunc, TimeSpan waitTimeBetweenPaExecExecute);
	}
}
