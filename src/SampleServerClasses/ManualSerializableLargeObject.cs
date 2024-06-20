using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NewRemoting.Toolkit;

namespace SampleServerClasses
{
	[Serializable]
	public class ManualSerializableLargeObject : IManualSerialization
	{
		[JsonIgnore]
		private byte[] _data;
		[JsonIgnore]
		private Memory<byte> _memory;

		public ManualSerializableLargeObject()
		{
			_data = null;
			_memory = new Memory<byte>();
		}

		public ManualSerializableLargeObject(Memory<byte> memory)
		{
			_memory = memory;
			_data = null;
		}

		[JsonIgnore]
		public int Length
		{
			get
			{
				return _data.Length;
			}
		}

		[JsonIgnore]
		public byte this[int index]
		{
			get
			{
				return _data[index];
			}
			set
			{
				_data[index] = value;
			}
		}

		public void Serialize(BinaryWriter serializerTarget)
		{
			serializerTarget.Write(_memory.Length);
			serializerTarget.Write(_memory.Span);
		}

		public void Deserialize(BinaryReader serializerSource)
		{
			int length = serializerSource.ReadInt32();
			_data = serializerSource.ReadBytes(length);
		}
	}
}
