using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NewRemoting
{
	internal class IpAddressSurrogate : JsonConverter<IPAddress>
	{
		public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartObject)
			{
				throw new JsonException($"Expected object, got {reader.TokenType}");
			}

			string address = null;
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndObject)
				{
					break;
				}

				string propertyName = reader.GetString()!;
				reader.Read();
				if (propertyName.Equals("Address", StringComparison.Ordinal))
				{
					address = reader.GetString();
				}
			}

			if (address == null || !IPAddress.TryParse(address, out var ip))
			{
				throw new JsonException("Not a valid type reference. Metadata item \"Address\" required.");
			}

			return ip;
		}

		public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			writer.WriteString("Address", value.ToString());
			writer.WriteEndObject();
		}
	}
}
