using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NewRemoting.Surrogates
{
	internal class ByteArraySurrogate : BlobSurrogate<byte[], byte[]>
	{
		public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			int length = reader.GetInt32();
			byte[] returnValue = new byte[length];
			RegisterForBinaryReader(returnValue);
			return returnValue;
		}

		public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
		{
			writer.WriteNumberValue(value.Length);
			RegisterForBinaryWriter(value);
		}

		protected override void Serialize(byte[] item, BinaryWriter w)
		{
			w.Write(item);
		}

		protected override void Deserialize(byte[] item, BinaryReader r)
		{
			int bytesRead = 0;
			while (bytesRead < item.Length)
			{
				int newB = r.Read(item, bytesRead, item.Length - bytesRead); // length is already known, but repeat until we have everything
				bytesRead += newB;
			}
		}
	}
}
