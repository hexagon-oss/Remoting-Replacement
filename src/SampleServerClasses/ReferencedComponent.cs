using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public class ReferencedComponent : MarshalByRefObject, IMyComponentInterface, IDisposable
	{
		private int _data;
		private Thread _timingThread;
		private bool _isThreadRunning;

		public event Action<DateTime, string> TimeChanged;

		public ReferencedComponent()
		{
			_data = GetHashCode();
			_timingThread = null;
			_isThreadRunning = false;
		}

		public string ComponentName
		{
			get;
			set;
		}

		public virtual int Data
		{
			get
			{
				return _data;
			}

			set
			{
				_data = value;
			}
		}

		public string ProcessName()
		{
			return Process.GetCurrentProcess().ProcessName;
		}

		public string ConfiguredName()
		{
			return ComponentName;
		}

		public virtual int SuperNumber()
		{
			return _data;
		}

		public DateTime QueryTime()
		{
			return DateTime.Now;
		}

		public Stream GetRemoteStream(string fileName)
		{
			return new FileStream(fileName, FileMode.Open);
		}

		public void StartTiming()
		{
			if (_timingThread == null)
			{
				_isThreadRunning = true;
				_timingThread = new Thread(DoReport);
				_timingThread.Start();
			}
		}

		private void DoReport()
		{
			while (_isThreadRunning)
			{
				try
				{
					TimeChanged?.Invoke(DateTime.Now, ProcessName());
				}
				catch (ObjectDisposedException)
				{
					return;
				}

				Thread.Sleep(1000);
			}
		}

		public void Dispose()
		{
			StopTiming();
		}

		public void StopTiming()
		{
			_isThreadRunning = false;
			if (_timingThread != null)
			{
				_timingThread.Join();
				_timingThread = null;
			}
		}
	}
}
