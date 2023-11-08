using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using NewRemoting.Toolkit;

namespace NewRemoting
{
	internal class ManualSerializerSurrogate : ISerializationSurrogate
	{
		private readonly InstanceManager _instanceManager;

		/// <summary>
		/// This serializer is not stateless!
		/// </summary>
		private int _index = 0;
		private List<IManualSerialization> _list = new List<IManualSerialization>();

		public ManualSerializerSurrogate(InstanceManager instanceManager)
		{
			_instanceManager = instanceManager;
		}

		public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
		{
			if (!(obj is IManualSerialization manual))
			{
				throw new InvalidOperationException("Instance must be of type IManualSerialization");
			}

			info.AddValue("AssemblyQualifiedName", obj.GetType().AssemblyQualifiedName);
			info.AddValue("PostSerialization", _index++);
			_list.Add(manual);
		}

		public void PerformManualSerialization(BinaryWriter w)
		{
			foreach (var item in _list)
			{
				item.Serialize(w);
			}
		}

		public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
		{
			// The instance has actually already been created
			_list.Add(obj as IManualSerialization);
			return obj;
		}

		public void PerformManualDeserialization(BinaryReader r)
		{
			foreach (var item in _list)
			{
				item.Deserialize(r);
			}
		}
	}
}
