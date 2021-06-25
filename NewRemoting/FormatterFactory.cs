using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	public class FormatterFactory : SurrogateSelector, ISurrogateSelector
	{
		private readonly InstanceManager _instanceManager;
		private readonly MySerializationSurrogate _serializationSurrogate;

		public FormatterFactory(InstanceManager instanceManager)
		{
			_instanceManager = instanceManager;
			_serializationSurrogate = new MySerializationSurrogate(_instanceManager);
		}

		public IFormatter CreateFormatter()
		{
			var bf = new BinaryFormatter(this, new StreamingContext(StreamingContextStates.All));
			return bf;
		}

		public override ISerializationSurrogate GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
		{
			// If the type being serialized is MarshalByRef and not serializable (having both is rare, but not impossible),
			// we redirect here and store an object reference.
			if (type.IsSubclassOf(typeof(MarshalByRefObject)) && !type.IsSerializable)
			{
				selector = this;
				return _serializationSurrogate;
			}

			return base.GetSurrogate(type, context, out selector);
		}

		private sealed class MySerializationSurrogate : ISerializationSurrogate
		{
			private readonly InstanceManager _instanceManager;

			public MySerializationSurrogate(InstanceManager instanceManager)
			{
				_instanceManager = instanceManager;
			}

			public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
			{
				string objectId = _instanceManager.GetIdForObject(obj);
				info.AddValue("ObjectId", objectId);
				info.AddValue("AssemblyQualifiedName", obj.GetType().AssemblyQualifiedName);
			}

			public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
			{
				string objectId = info.GetString("ObjectId");
				string typeName = info.GetString("AssemblyQualifiedName");
				// We don't know better here. We do not know what the static type of the field is that will store this reference.
				Type targetType = Server.GetTypeFromAnyAssembly(typeName);
				object newProxy = _instanceManager.CreateOrGetReferenceInstance(null, false, targetType, typeName, objectId);
				return newProxy;
			}
		}
	}
}
