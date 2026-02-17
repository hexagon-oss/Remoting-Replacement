using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	/// <summary>
	/// This doesn't really _need_ external serialization, but we use it for testing that
	/// </summary>
	public class ClassRequiringExternalSerialization
	{
		public ClassRequiringExternalSerialization(string data1)
		{
			Data1 = data1;
			WasSerialized = false;
		}

		public string Data1 { get; set; }

		public bool WasSerialized
		{
			get;
			set;
		}
	}
}
