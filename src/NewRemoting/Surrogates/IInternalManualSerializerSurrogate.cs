using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting.Surrogates
{
	internal interface IInternalManualSerializerSurrogate
	{
		void PerformManualSerialization(BinaryWriter w);
		void PerformManualDeserialization(BinaryReader r);
	}
}
