using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using NewRemoting.Toolkit;

namespace NewRemoting
{
	internal class FormatterFactory : SurrogateSelector, ISurrogateSelector
	{
		private readonly InstanceManager _instanceManager;
		private readonly ProxySurrogate _serializationSurrogate;
		private readonly CustomSerializerSurrogate _customSerializer;
		private readonly ConcurrentDictionary<string, BinaryFormatter> _cusBinaryFormatters;
		private readonly ConcurrentDictionary<int, ManualSerializerSurrogate> _manualSerializers;

		public FormatterFactory(InstanceManager instanceManager)
		{
			_instanceManager = instanceManager;
			_serializationSurrogate = new ProxySurrogate(_instanceManager);
			_customSerializer = new CustomSerializerSurrogate();
			_cusBinaryFormatters = new ConcurrentDictionary<string, BinaryFormatter>();
			_manualSerializers = new ConcurrentDictionary<int, ManualSerializerSurrogate>();
		}

		public IFormatter CreateOrGetFormatter(string otherSideProcessId)
		{
			if (_cusBinaryFormatters.TryGetValue(otherSideProcessId, out var formatter))
			{
				return formatter;
			}

			// Doing this twice doesn't hurt (except for a very minor performance penalty)
			var bf = new BinaryFormatter(this, new StreamingContext(StreamingContextStates.All, otherSideProcessId));
			_cusBinaryFormatters.TryAdd(otherSideProcessId, bf);
			return bf;
		}

		public override ISerializationSurrogate GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
		{
			// If the type being serialized is MarshalByRef and not serializable (having both is rare, but not impossible),
			// we redirect here and store an object reference.
			if ((type.IsSubclassOf(typeof(MarshalByRefObject)) || type == typeof(MarshalByRefObject)) && !type.IsSerializable)
			{
				selector = this;
				return _serializationSurrogate;
			}
			else if (type.IsAssignableTo(typeof(IManualSerialization)) && type.IsSerializable)
			{
				selector = this;
				if (_manualSerializers.TryGetValue(Thread.CurrentThread.ManagedThreadId, out var serializer))
				{
					return serializer;
				}

				var newserializer = new ManualSerializerSurrogate(_instanceManager);
				_manualSerializers.TryAdd(Thread.CurrentThread.ManagedThreadId, newserializer); // Cannot really fail
				return newserializer;
			}
			else if (Client.IsProxyType(type))
			{
				selector = this;
				return _serializationSurrogate;
			}

			if (_customSerializer.CanSerialize(type))
			{
				selector = this;
				return _customSerializer;
			}

			return base.GetSurrogate(type, context, out selector);
		}

		public void FinalizeSerialization(BinaryWriter w)
		{
			if (_manualSerializers.TryRemove(Thread.CurrentThread.ManagedThreadId, out var hasSerializer))
			{
				hasSerializer.PerformManualSerialization(w);
			}
		}

		public void FinalizeDeserialization(BinaryReader r)
		{
			if (_manualSerializers.TryRemove(Thread.CurrentThread.ManagedThreadId, out var hasSerializer))
			{
				hasSerializer.PerformManualDeserialization(r);
			}
		}

		private sealed class ProxySurrogate : ISerializationSurrogate
		{
			private readonly InstanceManager _instanceManager;

			public ProxySurrogate(InstanceManager instanceManager)
			{
				_instanceManager = instanceManager;
			}

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
		}
	}
}
