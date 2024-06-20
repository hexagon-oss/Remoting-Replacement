using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	[Serializable]
	public class CustomStructWithoutSetters
	{
		public CustomStructWithoutSetters(int data1, double data2)
		{
			Data1 = data1;
			Data2 = data2;
		}

		public int Data1
		{
			get;
		}

		public double Data2
		{
			get;
		}
	}
}
