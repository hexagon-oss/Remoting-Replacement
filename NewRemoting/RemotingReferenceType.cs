using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	public enum RemotingReferenceType
	{
		Undefined = 0,
		SerializedItem,
		RemoteReference,
		MethodPointer,
		InstanceOfSystemType,
		ArrayOfSystemType,
		NullPointer
	}
}
