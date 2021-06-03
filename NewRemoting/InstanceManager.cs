using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	public class InstanceManager
	{
		public InstanceManager()
		{
			InstanceIdentifier = Environment.MachineName + "/"+Environment.ProcessId.ToString(CultureInfo.CurrentCulture);
		}

		public string InstanceIdentifier
		{
			get;
		}

		public string GetIdForObject(object instance)
		{
			throw new NotImplementedException();
		}

		public bool TryGetObjectFromId(string id, out object instance)
		{
			throw new NotImplementedException();
		}

		public bool IsRemoteInstanceId(string objectId)
		{
			throw new NotImplementedException();
		}

		public void AddInstance(object instance, string objectId)
		{
			throw new NotImplementedException();
		}
	}
}
