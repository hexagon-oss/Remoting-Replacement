using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace NewRemoting
{
	internal sealed class ProxySurrogate : JsonConverter<object>
	{
		private readonly InstanceManager _instanceManager;
		private readonly string _otherSideInstanceId;

		public ProxySurrogate(InstanceManager instanceManager, string otherSideInstanceId)
		{
			_instanceManager = instanceManager;
			_otherSideInstanceId = otherSideInstanceId;
		}

		public override bool CanConvert(Type typeToConvert)
		{
			return MessageHandler.IsMarshalByRefType(typeToConvert);
		}

		public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
		{
			string objectId;
			if (Client.IsRemoteProxy(value))
			{
				// This should have an unit test, but I have not yet found out what test code causes this situation
				if (!_instanceManager.TryGetObjectId(value, out objectId, out Type originalType))
				{
					throw new RemotingException("Couldn't find matching objectId, although should be there");
				}

				writer.WriteStartObject();
				writer.WriteString("AssemblyQualifiedName", string.Empty);
				writer.WriteString("ObjectId", objectId);
				writer.WriteEndObject();
			}
			else
			{
				objectId = _instanceManager.RegisterRealObjectAndGetId(value, _otherSideInstanceId);
				writer.WriteStartObject();
				writer.WriteString("AssemblyQualifiedName", value.GetType().AssemblyQualifiedName);
				writer.WriteString("ObjectId", objectId);
				writer.WritePropertyName("Interfaces");
				writer.WriteStartArray();
				foreach (var elem in value.GetType().GetInterfaces().Where(x => x.IsPublic))
				{
					writer.WriteStringValue(elem.AssemblyQualifiedName);
				}

				writer.WriteEndArray();
				writer.WriteEndObject();
			}
		}

		public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartObject)
			{
				throw new JsonException($"Expected object, got {reader.TokenType}");
			}

			string objectId = null;
			string typeName = null;
			List<string> interfaceList = new List<string>();
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndObject)
				{
					break;
				}

				string propertyName = reader.GetString()!;
				reader.Read();
				string propertyValue = reader.GetString();
				if (propertyName.Equals("ObjectId", StringComparison.Ordinal))
				{
					objectId = propertyValue;
				}
				else if (propertyName.Equals("AssemblyQualifiedName", StringComparison.Ordinal))
				{
					typeName = propertyValue;
				}
				else
				{
					throw new JsonException($"Unknown property {propertyName} seen");
				}
			}

			if (objectId == null || typeName == null)
			{
				throw new JsonException("Invalid reference found");
			}

			// We don't know better here. We do not know what the static type of the field is that will store this reference.
			object newProxy;
			if (!string.IsNullOrEmpty(typeName))
			{
				Type targetType = Server.GetTypeFromAnyAssembly(typeName);
				newProxy = _instanceManager.CreateOrGetProxyForObjectId(null, false, targetType, typeName, objectId, interfaceList);
			}
			else
			{
				newProxy = _instanceManager.CreateOrGetProxyForObjectId(null, false, null, typeName, objectId, interfaceList);
			}

			return newProxy;
		}
	}
}
