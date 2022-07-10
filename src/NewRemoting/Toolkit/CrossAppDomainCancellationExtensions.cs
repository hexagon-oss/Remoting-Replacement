using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	public static class CrossAppDomainCancellationExtensions
	{
		public static CancellationToken GetLocalCancellationToken(this ICrossAppDomainCancellationToken token)
		{
			var ts = new CancellationTokenSource();
			token.Register(() => ts.Cancel());
			return ts.Token;
		}
	}
}
