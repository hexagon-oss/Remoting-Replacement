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

		protected RemotingException(
			SerializationInfo info,
			StreamingContext context)
			: base(info, context)
		{
		}

		public RemotingExceptionAdditionalInfo? AdditionalInfo
		{
			get => _additionalInfo;
			set => _additionalInfo = value;
		}
	}
}
