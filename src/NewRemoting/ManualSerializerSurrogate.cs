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

namespace NewRemoting
{
	internal class ManualSerializerSurrogate : JsonConverter<object>
	{
		private readonly InstanceManager _instanceManager;

		private ConcurrentDictionary<int, List<IManualSerialization>> _list = new();

		public ManualSerializerSurrogate(InstanceManager instanceManager)
		{
			_instanceManager = instanceManager;
		}

		public override bool CanConvert(Type typeToConvert)
		{
			return typeToConvert.IsAssignableTo(typeof(IManualSerialization));
		}

		public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
		{
			int threadId = Thread.CurrentThread.ManagedThreadId;
			writer.WriteStringValue(value.GetType().AssemblyQualifiedName);
			if (_list.TryGetValue(threadId, out var myList))
			{
				myList.Add((IManualSerialization)value);
			}
			else
			{
				_list.TryAdd(threadId, new List<IManualSerialization>() { (IManualSerialization)value });
			}
		}

		/*
		public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
		{
			if (!(obj is IManualSerialization manual))
			{
				throw new InvalidOperationException("Instance must be of type IManualSerialization");
			}

			info.AddValue("AssemblyQualifiedName", obj.GetType().AssemblyQualifiedName);
			_list.Add(manual);
		}*/

		public void PerformManualSerialization(BinaryWriter w)
		{
			int threadId = Thread.CurrentThread.ManagedThreadId;
			if (_list.TryRemove(threadId, out var myList))
			{
				foreach (var item in myList)
				{
					item.Serialize(w);
				}
			}
		}

		public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			string objectType = reader.GetString();
			Type t = Server.GetTypeFromAnyAssembly(objectType);
			var defaultCtor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Array.Empty<Type>());
			if (defaultCtor == null)
			{
				throw new RemotingException($"No default constructor on type {typeToConvert} found");
			}

			int threadId = Thread.CurrentThread.ManagedThreadId;
			IManualSerialization manual = (IManualSerialization)defaultCtor.Invoke(Array.Empty<object>());
			if (_list.TryGetValue(threadId, out var myList))
			{
				myList.Add(manual);
			}
			else
			{
				_list.TryAdd(threadId, new List<IManualSerialization>() { manual });
			}

			return manual;
		}

		/*
		public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
		{
			// The instance has actually already been created
			_list.Add(obj as IManualSerialization);
			return obj;
		}
		*/

		public void PerformManualDeserialization(BinaryReader r)
		{
			int threadId = Thread.CurrentThread.ManagedThreadId;
			if (_list.TryRemove(threadId, out var myList))
			{
				foreach (var item in myList)
				{
					item.Deserialize(r);
				}
			}
		}
	}
}
