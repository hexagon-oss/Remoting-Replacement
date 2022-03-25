using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NewRemoting
{
	public class SimpleLogFileWriter : ILogger, IDisposable
	{
		private readonly string _loggerName;
		private readonly LogLevel _minLogLevel;
		private readonly TextWriter _writer;
		private readonly ScopeDisposable _emptyDisposable;

		public SimpleLogFileWriter(string file, string loggerName, LogLevel minLogLevel = LogLevel.Information)
		{
			_emptyDisposable = new ScopeDisposable();
			_loggerName = loggerName;
			_minLogLevel = minLogLevel;
			_writer = new StreamWriter(file, Encoding.Unicode, new FileStreamOptions() { Access = FileAccess.Write, Mode = FileMode.CreateNew });
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			if (formatter != null)
			{
				string msg = formatter(state, exception);
				string formattedTime = DateTime.UtcNow.ToString("O").Replace("T", " ").Replace("Z", string.Empty);
				string formatted = FormattableString.Invariant($"\"{formattedTime}\"; \"{logLevel}\"; \"{_loggerName}\"; \"{msg}\"; \"{exception}\"");
				_writer.WriteLine(formatted);
			}
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return logLevel >= _minLogLevel;
		}

		public IDisposable BeginScope<TState>(TState state)
		{
			return _emptyDisposable;
		}

		protected virtual void Dispose(bool disposing)
		{
			_writer.Dispose();
		}

		public void Dispose()
		{
			Dispose(true);
		}

		private sealed class ScopeDisposable : IDisposable
		{
			public void Dispose()
			{
			}
		}
	}
}
