using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	[Serializable]
	public class RemotingException : Exception
	{
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
	}

}
