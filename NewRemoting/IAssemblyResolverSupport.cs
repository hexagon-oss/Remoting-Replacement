using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	public interface IAssemblyResolverSupport
	{
		public object CreateInstance(Type type, out Assembly assembly);
	}
}
