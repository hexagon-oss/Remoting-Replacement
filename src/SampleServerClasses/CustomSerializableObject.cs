using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	[Serializable]
	public struct CustomSerializableObject
	{
		public int Value { get; set; }

		public DateTime Time { get; set; }
	}
}
