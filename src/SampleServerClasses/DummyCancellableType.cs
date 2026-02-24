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

		public virtual void WaitForToken(ICrossAppDomainCancellationToken cancellationToken)
		{
			WaitForCancellation(cancellationToken.GetLocalCancellationToken());
		}

		public virtual void DoSomethingWithNormalToken(CancellationToken cancellationToken)
		{
			WaitForCancellation(cancellationToken);
		}

		private void WaitForCancellation(CancellationToken token)
		{
			Stopwatch w = Stopwatch.StartNew();
			while (true)
			{
				if (w.Elapsed > TimeSpan.FromSeconds(20))
				{
					return;
				}

				token.ThrowIfCancellationRequested();
				Thread.Sleep(100);
			}
		}
	}
}
