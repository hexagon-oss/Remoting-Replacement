using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	[Serializable]
	public class SerializableClassWithMarshallableMembers
	{
		public SerializableClassWithMarshallableMembers(int idx, ReferencedComponent component)
		{
			Idx = idx;
			Component = component;
		}

		public int Idx { get; }
		public ReferencedComponent Component { get; }

		public virtual int CallbackViaComponent()
		{
			return Component.Data;
		}

		public virtual SerializableClassWithMarshallableMembers ReturnSelfToCaller()
		{
			return this;
		}
	}
}
