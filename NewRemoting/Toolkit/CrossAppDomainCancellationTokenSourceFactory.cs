using System;

namespace NewRemoting.Toolkit
{
	public sealed class CrossAppDomainCancellationTokenSourceFactory : MarshalByRefObject, ICrossAppDomainCancellationTokenSourceFactory
	{
		ICrossAppDomainCancellationTokenSource ICrossAppDomainCancellationTokenSourceFactory.Create()
		{
			return new CrossAppDomainCancellationTokenSource();
		}
	}
}
