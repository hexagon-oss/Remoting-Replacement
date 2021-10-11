using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewRemoting.Toolkit;

namespace SampleServerClasses
{
	public class DummyCancellableType : MarshalByRefObject
	{
		public DummyCancellableType()
		{
			Factory = new CrossAppDomainCancellationTokenSourceFactory();
		}

		public virtual ICrossAppDomainCancellationTokenSourceFactory Factory
		{
			get;
		}

		public virtual void DoSomething(ICrossAppDomainCancellationToken cancellationToken)
		{
			cancellationToken.LocalToken.ThrowIfCancellationRequested();
		}
	}
}
