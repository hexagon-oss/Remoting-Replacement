namespace NewRemoting.Toolkit
{
	/// <summary>
	/// Factory to create remoting capable cancellation token source. <see cref="ICrossAppDomainCancellationTokenSource"/>.
	/// </summary>
	public interface ICrossAppDomainCancellationTokenSourceFactory
	{
		/// <summary>
		/// Creates a new token source.
		/// </summary>
		ICrossAppDomainCancellationTokenSource Create();
	}
}
