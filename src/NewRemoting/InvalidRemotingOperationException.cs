using System;
using System.Runtime.Serialization;

namespace NewRemoting;

[Serializable]
public class InvalidRemotingOperationException : RemotingException
{
	public InvalidRemotingOperationException()
	{
	}

	public InvalidRemotingOperationException(string message)
		: base(message)
	{
	}

	public InvalidRemotingOperationException(string message, Exception inner)
		: base(message, inner)
	{
	}
}
