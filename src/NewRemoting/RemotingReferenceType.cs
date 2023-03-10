using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	/// <summary>
	/// Header word in the data stream that identifies the parameter that follows.
	/// The protocol does not use a length field or a tailer, so if the data size does not fit, the stream will loose
	/// sync and the application will crash.
	/// </summary>
	internal enum RemotingReferenceType
	{
		Undefined = 0,
		Auto,
		SerializedItem,
		RemoteReference,
		InstanceOfSystemType,
		ArrayOfSystemType,
		NullPointer,
		ContainerType,
		IpAddress,
		Int8,
		Uint8,
		Int16,
		Uint16,
		Int32,
		Uint32,
		Int64,
		Uint64,
		BoolTrue,
		BoolFalse,
		Float,
		Double,
		Half,
		String,
		AddEvent,
		RemoveEvent,
		MethodPointer
	}
}
