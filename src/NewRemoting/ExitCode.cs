namespace NewRemoting
{
	public enum ExitCode
	{
		Success = 0,
		StartFailure = -1, // can happen when certificate file does not exist
		SocketCreationFailure = -2 // socket port already in used
	}
}
