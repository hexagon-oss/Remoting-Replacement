using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NewRemoting.Surrogates
{
	internal class InterfaceInstantiationSurrogate : JsonConverter<object>
	{
		public override bool CanConvert(Type typeToConvert)
		{
			// A non-marshal-by-ref type and an interface
			return typeToConvert.IsAssignableTo(typeof(MarshalByRefObject)) == false && typeToConvert.IsInterface && Client.IsProxyType(typeToConvert) == false;
		}

		public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartObject)
			{
				throw new JsonException($"Expected object, got {reader.TokenType}");
			}

			string typeName = null;
			object ret = null;
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
					string propertyValue = reader.GetString();
					typeName = propertyValue;
				}
				else if (propertyName.Equals("Instance", StringComparison.Ordinal) && typeName != null)
				{
					Type concreteType = Server.GetTypeFromAnyAssembly(typeName);
					ret = JsonSerializer.Deserialize(ref reader, concreteType, options);
				}
				else
				{
					throw new JsonException($"Unknown property {propertyName} seen");
				}
			}

			if (ret == null)
			{
				throw new JsonException("Incomplete object json found");
			}

			return ret;
		}

		public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			writer.WriteString("AssemblyQualifiedName", value.GetType().AssemblyQualifiedName);
			writer.WritePropertyName("Instance");
			JsonSerializer.Serialize(writer, value, options);
			writer.WriteEndObject();
		}
	}
}
