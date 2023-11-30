using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using NewRemoting.Toolkit;

namespace NewRemoting
{
	internal class FormatterFactory
	{
		private readonly InstanceManager _instanceManager;
		private readonly ConcurrentDictionary<string, JsonSerializerOptions> _cusBinaryFormatters;

		public FormatterFactory(InstanceManager instanceManager)
		{
			_instanceManager = instanceManager;
			_cusBinaryFormatters = new ConcurrentDictionary<string, JsonSerializerOptions>();
		}

		public JsonSerializerOptions CreateOrGetFormatter(string otherSideProcessId)
		{
			if (_cusBinaryFormatters.TryGetValue(otherSideProcessId, out var formatter))
			{
				return formatter;
			}

			// Doing this twice doesn't hurt (except for a very minor performance penalty)
			JsonSerializerOptions options = new JsonSerializerOptions()
			{
				IncludeFields = true,
				Converters =
				{
					new ProxySurrogate(_instanceManager, otherSideProcessId),
					new ManualSerializerSurrogate(_instanceManager),
					new CultureInfoSerializerSurrogate(),
				}
			};

			_cusBinaryFormatters.TryAdd(otherSideProcessId, options);
			return options;
		}

		public void FinalizeSerialization(BinaryWriter w, JsonSerializerOptions formatter)
		{
			var manualSerializer = (ManualSerializerSurrogate)formatter.Converters.First(x => x is ManualSerializerSurrogate);
			manualSerializer.PerformManualSerialization(w);
		}

		public void FinalizeDeserialization(BinaryReader r, JsonSerializerOptions formatter)
		{
			var manualSerializer = (ManualSerializerSurrogate)formatter.Converters.First(x => x is ManualSerializerSurrogate);
			manualSerializer.PerformManualDeserialization(r);
		}

		private sealed class ProxySurrogate : JsonConverter<object>
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
				return base.CanConvert(typeToConvert) && typeToConvert.IsAssignableTo(typeof(MarshalByRefObject));
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
				while (reader.Read())
				{
					if (reader.TokenType == JsonTokenType.EndObject)
					{
						break;
					}

					string propertyName = reader.GetString()!;
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
					newProxy = _instanceManager.CreateOrGetProxyForObjectId(null, false, targetType, typeName, objectId);
				}
				else
				{
					newProxy = _instanceManager.CreateOrGetProxyForObjectId(null, false, null, typeName, objectId);
				}

				return newProxy;
			}

			/*
			public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
			{
				string objectId;
				if (Client.IsRemoteProxy(obj))
				{
					// This should have an unit test, but I have not yet found out what test code causes this situation
					if (!_instanceManager.TryGetObjectId(obj, out objectId, out Type originalType))
					{
						throw new RemotingException("Couldn't find matching objectId, although should be there");
					}

					// The proxy's assembly name is "DynamicProxyGenAssembly2", which does not physically exist and is certainly different on the
					// remote side. Therefore make sure we never pass that name in the serialization stream.
					info.AssemblyName = originalType.Assembly.FullName;
					info.FullTypeName = originalType.FullName;
					info.AddValue("ObjectId", objectId);
					info.AddValue("AssemblyQualifiedName", string.Empty);
				}
				else
				{
					objectId = _instanceManager.RegisterRealObjectAndGetId(obj, (string)context.Context);
					info.AddValue("ObjectId", objectId);
					info.AddValue("AssemblyQualifiedName", obj.GetType().AssemblyQualifiedName);
				}
			}

			public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
			{
				string objectId = info.GetString("ObjectId");
				string typeName = info.GetString("AssemblyQualifiedName");
				// We don't know better here. We do not know what the static type of the field is that will store this reference.
				object newProxy;
				if (!string.IsNullOrEmpty(typeName))
				{
					Type targetType = Server.GetTypeFromAnyAssembly(typeName);
					newProxy = _instanceManager.CreateOrGetProxyForObjectId(null, false, targetType, typeName, objectId);
				}
				else
				{
					newProxy = _instanceManager.CreateOrGetProxyForObjectId(null, false, null, typeName, objectId);
				}

				return newProxy;
			}
			*/
		}
	}
}
