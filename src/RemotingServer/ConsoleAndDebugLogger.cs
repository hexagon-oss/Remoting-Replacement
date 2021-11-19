using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace RemotingServer
{
	internal class ConsoleAndDebugLogger : ILogger
	{
		private readonly string _categoryName;

		public ConsoleAndDebugLogger(string categoryName)
		{
			_categoryName = categoryName;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
			Func<TState, Exception, string> formatter)
		{
			if (formatter != null)
			{
				string msg = formatter(state, exception);
				Console.WriteLine(msg);
				Debug.WriteLine(msg);
			}
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public IDisposable BeginScope<TState>(TState state)
		{
			return new DummyDisposable();
		}

		internal sealed class DummyDisposable : IDisposable
		{
			public void Dispose()
			{
			}
		}
	}
}
