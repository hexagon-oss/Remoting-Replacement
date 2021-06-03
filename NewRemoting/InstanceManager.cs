using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	public class InstanceManager
	{
		private ConcurrentDictionary<string, InstanceInfo> _objects;

		public InstanceManager()
		{
			InstanceIdentifier = Environment.MachineName + "/"+Environment.ProcessId.ToString(CultureInfo.CurrentCulture);
			_objects = new();
		}

		public string InstanceIdentifier
		{
			get;
		}

		public string GetIdForObject(object instance)
		{
			string id = CreateObjectInstanceId(instance);
			AddInstance(instance, id);
			return id;
		}

		public bool TryGetObjectFromId(string id, [NotNullWhen(true)]out object instance)
		{
			if (_objects.TryGetValue(id, out InstanceInfo value))
			{
				if (value.Instance != null)
				{
					instance = value.Instance;
					return true;
				}
			}

			instance = null;
			return false;
		}

		public bool IsLocalInstanceId(string objectId)
		{
			return objectId.StartsWith(InstanceIdentifier);
		}

		public void AddInstance(object instance, string objectId)
		{
			_objects.AddOrUpdate(objectId, s => new InstanceInfo(instance, objectId), (s, info) => new InstanceInfo(instance, objectId));
		}

		/// <summary>
		/// This method is slow and should only be used for debugging purposes (invariant validation)
		/// </summary>
		public bool TryGetObjectId(object instance, out string instanceId)
		{
			var values = _objects.Values.ToList();
			foreach (var v in values)
			{
				if (ReferenceEquals(v.Instance, instance))
				{
					instanceId = v.Identifier;
					return true;
				}
			}

			instanceId = null;
			return false;
		}

		public object GetObjectFromId(string id)
		{
			if (!TryGetObjectFromId(id, out object instance))
			{
				throw new InvalidOperationException($"Could not locate instance with ID {id} or it is not local");
			}

			return instance;
		}

		private string CreateObjectInstanceId(object obj)
		{
			string objectReference = FormattableString.Invariant($"{InstanceIdentifier}/{obj.GetType().FullName}/{RuntimeHelpers.GetHashCode(obj)}");
			Console.WriteLine($"Created object reference with id {objectReference}");
			return objectReference;
		}

		private class InstanceInfo
		{
			public InstanceInfo(object obj, string identifier)
			{
				Instance = obj;
				Identifier = identifier;
			}

			public object Instance
			{
				get;
			}

			public string Identifier
			{
				get;
			}
		}
	}
}
