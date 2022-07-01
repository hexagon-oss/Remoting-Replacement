using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public class SimpleCalc : MarshalByRefObject
	{
		public virtual double Add(double a, double b) => a + b;

		public virtual double Sub(double a, double b) => a - b;

		public virtual void DooFoo(SimpleCalc other)
		{
			other.Sub(2, 2);
		}
	}
}
