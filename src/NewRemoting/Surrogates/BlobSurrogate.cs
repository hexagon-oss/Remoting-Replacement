using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NewRemoting.Toolkit;

namespace NewRemoting.Surrogates
{
	internal abstract class BlobSurrogate<TType, TContainer> : JsonConverter<TType>, IInternalManualSerializerSurrogate
	{
		private readonly ConcurrentDictionary<int, List<TContainer>> _list = new ConcurrentDictionary<int, List<TContainer>>();

		public BlobSurrogate()
		{
		}

		public void PerformManualSerialization(BinaryWriter w)
		{
			int threadId = Thread.CurrentThread.ManagedThreadId;
			if (_list.TryRemove(threadId, out var myList))
			{
				foreach (var item in myList)
				{
					Serialize(item, w);
				}
			}
		}

		protected abstract void Serialize(TContainer item, BinaryWriter w);

		public void PerformManualDeserialization(BinaryReader r)
		{
			int threadId = Thread.CurrentThread.ManagedThreadId;
			if (_list.TryRemove(threadId, out var myList))
			{
				foreach (var item in myList)
				{
					Deserialize(item, r);
				}
			}
		}

		protected abstract void Deserialize(TContainer item, BinaryReader r);

		protected void RegisterForBinaryWriter(object value)
		{
			int threadId = Thread.CurrentThread.ManagedThreadId;
			if (_list.TryGetValue(threadId, out var myList))
			{
				myList.Add((TContainer)value);
			}
			else
			{
				_list.TryAdd(threadId, new List<TContainer>() { (TContainer)value });
			}
		}

		protected void RegisterForBinaryReader(TContainer returnValue)
		{
			int threadId = Thread.CurrentThread.ManagedThreadId;
			if (_list.TryGetValue(threadId, out var myList))
			{
				myList.Add(returnValue);
			}
			else
			{
				_list.TryAdd(threadId, new List<TContainer>() { returnValue });
			}
		}
	}
}
