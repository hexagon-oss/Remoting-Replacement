using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewRemoting.Toolkit;

namespace SampleServerClasses
{
	public class DummyCancellableType : MarshalByRefObject
	{
		public DummyCancellableType()
		{
		}

		public virtual void DoSomething(ICrossAppDomainCancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
		}

		public virtual void DoSomethingWithNormalToken(ICrossAppDomainCancellationToken cancellationToken)
		{
			TakeToken(cancellationToken.GetLocalCancellationToken());
		}

		private void TakeToken(CancellationToken token)
		{
			Stopwatch w = Stopwatch.StartNew();
			while (true)
			{
				if (w.Elapsed > TimeSpan.FromSeconds(20))
				{
					return;
				}

				token.ThrowIfCancellationRequested();
			}
		}
	}
}
