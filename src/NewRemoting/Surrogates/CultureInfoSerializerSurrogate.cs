using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewRemoting.Surrogates
{
	/// <summary>
	/// Provides serialization for some system types that are not marked as serializable but for which there is a more or less obvious way to serialize them
	/// </summary>
	internal class CultureInfoSerializerSurrogate : JsonConverter<CultureInfo>
	{
		public override void Write(Utf8JsonWriter writer, CultureInfo value, JsonSerializerOptions options)
		{
			writer.WriteStringValue(value.Name);
		}

		public override CultureInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			string value = reader.GetString();
			return new CultureInfo(value!);
		}
	}
}
