using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	/// <summary>
	/// This interface is a wrapper around <see cref="System.Diagnostics.Process"/>, to simplify adding additional properties and allow easier testing
	/// </summary>
	public interface IProcess : IDisposable
	{
		event DataReceivedEventHandler ErrorDataReceived;
		event DataReceivedEventHandler OutputDataReceived;
		event EventHandler Exited;

		string ProcessName
		{
			get;
		}

		ProcessStartInfo StartInfo
		{
			get;
			set;
		}

		bool Responding
		{
			get;
		}

		bool HasExited
		{
			get;
		}

		ProcessModule MainModule
		{
			get;
		}

		IntPtr MainWindowHandle
		{
			get;
		}

		int Id
		{
			get;
		}

		bool EnableRaisingEvents
		{
			get;
			set;
		}

		int ExitCode
		{
			get;
		}

		IntPtr Handle
		{
			get;
		}

		StreamReader StandardOutput
		{
			get;
		}

		StreamReader StandardError
		{
			get;
		}

		StreamWriter StandardInput
		{
			get;
		}

		long WorkingSet64
		{
			get;
		}

		bool Start();
		void Kill();
		bool WaitForExit(int milliseconds);
		void WaitForExit();
		bool CloseMainWindow();

		void BeginOutputReadLine();

		void BeginErrorReadLine();
	}
}
