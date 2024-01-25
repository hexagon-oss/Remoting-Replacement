using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NewRemoting
{
	/// <summary>
	/// Replaces an instance of <see cref="System.Type"/> in a json stream
	/// </summary>
	internal class SystemTypeSurrogate : JsonConverter<Type>
	{
		public override Type Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartObject)
			{
				throw new JsonException($"Expected object, got {reader.TokenType}");
			}

			string typeName = null;
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndObject)
				{
					break;
				}

				string propertyName = reader.GetString()!;
				reader.Read();
				if (propertyName.Equals("AssemblyQualifiedName", StringComparison.Ordinal))
				{
					typeName = reader.GetString();
				}
			}

			if (typeName == null)
			{
				throw new JsonException("Not a valid type reference. Metadata item \"AssemblyQualifiedName\" required.");
			}

			var type = Server.GetTypeFromAnyAssembly(typeName, true);
			return type;
		}

		public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			writer.WriteString("AssemblyQualifiedName", value.AssemblyQualifiedName);
			writer.WriteString("IsType", "true");
			writer.WriteEndObject();
		}
	}
}
