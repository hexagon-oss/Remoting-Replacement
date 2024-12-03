using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NewRemoting.Toolkit;

namespace NewRemoting.Surrogates
{
	internal class ManualSerializerSurrogate : BlobSurrogate<object, IManualSerialization>
	{
		public ManualSerializerSurrogate()
		{
		}

		public override bool CanConvert(Type typeToConvert)
		{
			return typeof(IManualSerialization).IsAssignableFrom(typeToConvert);
		}

		public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
		{
			writer.WriteStringValue(value.GetType().AssemblyQualifiedName);
			RegisterForBinaryWriter(value);
		}

		protected override void Deserialize(IManualSerialization item, BinaryReader r)
		{
			item.Deserialize(r);
		}

		protected override void Serialize(IManualSerialization item, BinaryWriter w)
		{
			item.Serialize(w);
		}

		public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			string objectType = reader.GetString();
			Type t = Server.GetTypeFromAnyAssembly(objectType);
			var defaultCtor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.DefaultBinder, Array.Empty<Type>(), Array.Empty<ParameterModifier>());
			if (defaultCtor == null)
			{
				throw new RemotingException($"No default constructor on type {typeToConvert} found");
			}

			IManualSerialization manual = (IManualSerialization)defaultCtor.Invoke(Array.Empty<object>());
			RegisterForBinaryReader(manual);

			return manual;
		}
	}
}
