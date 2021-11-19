using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NewRemoting
{
	public sealed class KeepAliveSponsor : IDisposable
	{
		private readonly object _instance;
		private readonly TimeSpan _timeout;

		public KeepAliveSponsor(object instance, TimeSpan timeout)
		{
			_instance = instance;
			_timeout = timeout;
		}

		public KeepAliveSponsor(object instance)
		: this(instance, Timeout.InfiniteTimeSpan)
		{
		}

		public void Dispose()
		{
		}
	}
}
