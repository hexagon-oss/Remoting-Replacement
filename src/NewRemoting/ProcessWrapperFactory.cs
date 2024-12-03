using System.Diagnostics;

namespace NewRemoting
{
	internal class ProcessWrapperFactory : IProcessWrapperFactory
	{
		public IProcess CreateProcess()
		{
			return new ProcessWrapper(new Process());
		}
	}
}
