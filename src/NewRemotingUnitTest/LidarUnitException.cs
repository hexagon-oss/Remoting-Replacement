using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemotingUnitTest
{
	public enum LidarUnitExceptionReason
	{
		Unknown,
		SensorCommunicationException,
		AdqDeviceException,
		IncompatibleProtocolVersion,
		InitFailedDueToInvalidState
	}

	public class LidarUnitException : Exception
	{
		public LidarUnitException()
		{
			Reason = LidarUnitExceptionReason.Unknown;
		}

		public LidarUnitException(string message)
			: this(message, LidarUnitExceptionReason.Unknown)
		{
		}

		public LidarUnitException(string message, Exception inner)
			: this(message, LidarUnitExceptionReason.Unknown, inner)
		{
		}

		public LidarUnitException(string message, LidarUnitExceptionReason reason)
			: base(message)
		{
			Reason = reason;
		}

		public LidarUnitException(string message, LidarUnitExceptionReason reason, Exception inner)
			: base(message, inner)
		{
			Reason = reason;
		}

		public LidarUnitExceptionReason Reason
		{
			get;
			protected set;
		}
	}
}
