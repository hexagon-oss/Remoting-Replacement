using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	public enum RemotingExceptionKind
	{
		Unknown,
		CommunicationError,
		ProtocolError,
		UnsupportedOperation,
		ProxyManagementError,
	}

	[Serializable]
	public class RemotingException : Exception
	{
		public RemotingException()
		{
			ExceptionKind = RemotingExceptionKind.Unknown;
		}

		public RemotingException(string message, RemotingExceptionKind exceptionKind)
			: base(message)
		{
			ExceptionKind = exceptionKind;
		}

		public RemotingException(string message, RemotingExceptionKind exceptionKind, Exception inner)
			: base(message, inner)
		{
			ExceptionKind = exceptionKind;
		}

		protected RemotingException(
			SerializationInfo info,
			StreamingContext context)
			: base(info, context)
		{
			ExceptionKind = (RemotingExceptionKind)info.GetValue(nameof(ExceptionKind), typeof(int));
		}

		public RemotingExceptionKind ExceptionKind { get; }

		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue(nameof(ExceptionKind), (int)ExceptionKind);
			base.GetObjectData(info, context);
		}
	}

}
