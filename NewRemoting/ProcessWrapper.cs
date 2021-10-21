using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	internal sealed class ProcessWrapper : IProcess
	{
		private Process _processImplementation;

		internal ProcessWrapper(Process wrappedInstance)
		{
			_processImplementation = wrappedInstance ?? throw new ArgumentNullException(nameof(wrappedInstance));
		}

		public event DataReceivedEventHandler ErrorDataReceived
		{
			add => _processImplementation.ErrorDataReceived += value;
			remove => _processImplementation.ErrorDataReceived -= value;
		}

		public event DataReceivedEventHandler OutputDataReceived
		{
			add => _processImplementation.OutputDataReceived += value;
			remove => _processImplementation.OutputDataReceived -= value;
		}

		public event EventHandler Exited
		{
			add => _processImplementation.Exited += value;
			remove => _processImplementation.Exited -= value;
		}

		public string ProcessName => _processImplementation.ProcessName;

		public ProcessStartInfo StartInfo
		{
			get => _processImplementation.StartInfo;
			set => _processImplementation.StartInfo = value;
		}

		public bool Responding => _processImplementation.Responding;

		public bool HasExited => _processImplementation.HasExited;

		public ProcessModule MainModule => _processImplementation.MainModule;

		public IntPtr MainWindowHandle => _processImplementation.MainWindowHandle;

		public int Id => _processImplementation.Id;

		public bool EnableRaisingEvents
		{
			get => _processImplementation.EnableRaisingEvents;
			set => _processImplementation.EnableRaisingEvents = value;
		}

		public int ExitCode => _processImplementation.ExitCode;
		public IntPtr Handle => _processImplementation.Handle;

		public StreamReader StandardOutput => _processImplementation.StandardOutput;

		public StreamReader StandardError => _processImplementation.StandardError;

		public StreamWriter StandardInput => _processImplementation.StandardInput;

		public long WorkingSet64 => _processImplementation.WorkingSet64;

		public bool Start()
		{
			return _processImplementation.Start();
		}

		public void Kill()
		{
			_processImplementation.Kill();
		}

		public bool WaitForExit(int milliseconds)
		{
			return _processImplementation.WaitForExit(milliseconds);
		}

		public void WaitForExit()
		{
			_processImplementation.WaitForExit();
		}

		public bool CloseMainWindow()
		{
			return _processImplementation.CloseMainWindow();
		}

		public void BeginOutputReadLine()
		{
			_processImplementation.BeginOutputReadLine();
		}

		public void BeginErrorReadLine()
		{
			_processImplementation.BeginErrorReadLine();
		}

		public void Dispose()
		{
			_processImplementation.Dispose();
		}

	}
}
