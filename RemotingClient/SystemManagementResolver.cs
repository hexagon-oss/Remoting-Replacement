using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NewRemoting;

namespace RemotingClient
{
	public class SystemManagementResolver : IAssemblyResolverSupport
	{
		public object CreateInstance(Type type, out Assembly assembly)
		{
			var obj = Activator.CreateInstance(type);
			assembly = obj.GetType().Assembly;
			return obj;
		}
	}
}
