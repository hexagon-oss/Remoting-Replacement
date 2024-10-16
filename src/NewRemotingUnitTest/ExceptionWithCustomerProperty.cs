using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemotingUnitTest
{
	public enum SomeExceptionReason
	{
		Unknown,
		SensorCommunicationException,
		AdqDeviceException,
		IncompatibleProtocolVersion,
		InitFailedDueToInvalidState
	}

	public class ExceptionWithCustomerProperty : Exception
	{
		public ExceptionWithCustomerProperty()
		{
			Reason = SomeExceptionReason.Unknown;
		}

		public ExceptionWithCustomerProperty(string message)
			: this(message, SomeExceptionReason.Unknown)
		{
		}

		public ExceptionWithCustomerProperty(string message, Exception inner)
			: this(message, SomeExceptionReason.Unknown, inner)
		{
		}

		public ExceptionWithCustomerProperty(string message, SomeExceptionReason reason)
			: base(message)
		{
			Reason = reason;
		}

		public ExceptionWithCustomerProperty(string message, SomeExceptionReason reason, Exception inner)
			: base(message, inner)
		{
			Reason = reason;
		}

		public SomeExceptionReason Reason
		{
			get;
			protected set;
		}
	}
}
