namespace NewRemoting
{
	public interface IRemoteConsoleFactory
	{
		IRemoteConsole Create(string remoteHost, Credentials remoteCredentials);
	}
}
