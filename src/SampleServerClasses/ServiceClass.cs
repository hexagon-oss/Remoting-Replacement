using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public class ServiceClass : MarshalByRefObject
	{
		public ServiceClass(ConstructorArgument argument)
		{
			if (argument == null || argument.ReverseInterface == null)
			{
				throw new ArgumentNullException(nameof(argument));
			}

			ReverseInterface = argument.ReverseInterface;
		}

		public virtual IMyComponentInterface ReverseInterface
		{
			get;
		}

		public virtual string DoSomething()
		{
			return "Wrapped by Server: " + ReverseInterface.ConfiguredName();
		}
	}
}
