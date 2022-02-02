using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public interface ITransientServer : IDisposable
	{
		void Init(int port);
		T GetTransientInterface<T>();

		T CreateTransientClass<T>()
			where T : MarshalByRefObject;
	}
}
