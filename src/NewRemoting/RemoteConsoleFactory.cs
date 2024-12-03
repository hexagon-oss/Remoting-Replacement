namespace NewRemoting
{
	public sealed class RemoteConsoleFactory : IRemoteConsoleFactory
	{
		IRemoteConsole IRemoteConsoleFactory.Create(string remoteHost, Credentials remoteCredentials)
		{
			return new RemoteConsole(remoteHost, remoteCredentials);
		}
	}
}
