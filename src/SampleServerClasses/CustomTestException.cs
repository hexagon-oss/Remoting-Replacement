using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public class CustomTestException : Exception
	{
		private CustomTestException(string message, Exception innerException)
			: base(message, innerException)
		{
		}

		public CustomTestException(string message, string customMessagePart, int numberOfErrors)
			: base(message)
		{
			CustomMessagePart = customMessagePart;
			NumberOfErrors = numberOfErrors;
		}

		public String CustomMessagePart
		{
			get;
			private set;
		}

		public int NumberOfErrors
		{
			get;
			private set;
		}
	}
}
