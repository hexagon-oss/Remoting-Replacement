using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	/// <summary>
	/// Provides serialization for some system types that are not marked as serializable but for which there is a more or less obvious way to serialize them
	/// </summary>
	internal class CustomSerializerSurrogate : ISerializationSurrogate
	{
		public bool CanSerialize(Type type)
		{
			if (type == typeof(CultureInfo))
			{
				return true;
			}

			return false;
		}

		public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
		{
			if (obj is CultureInfo ci)
			{
				// Only use the name, not the whole settings here
				info.AddValue("Culture", ci.Name);
			}
			else
			{
				throw new InvalidOperationException("Unknown type for Custom Serialization");
			}
		}

		public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
		{
			if (obj is CultureInfo)
			{
				return new CultureInfo(info.GetString("Culture"));
			}
			else
			{
				throw new InvalidOperationException("Unknown type for Custom Deserialization");
			}
		}
	}
}
