using System;
using System.Runtime.Serialization;

namespace NewRemoting
{
	[Serializable]
	public class RemotingException : Exception
	{
		private RemotingExceptionAdditionalInfo? _additionalInfo = null;
		public RemotingException()
		{
		}

		public RemotingException(string message)
			: base(message)
		{
		}

		public RemotingException(string message, Exception inner)
			: base(message, inner)
		{
		}
	}

		public RemotingExceptionAdditionalInfo? AdditionalInfo
		{
			get => _additionalInfo;
			set => _additionalInfo = value;
		}
	}
}
