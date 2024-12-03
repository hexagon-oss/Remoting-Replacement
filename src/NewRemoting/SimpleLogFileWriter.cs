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
			FileInfo fileInfo = new FileInfo(file);
			if (!IsDirectoryWritable(fileInfo.DirectoryName))
			{
				file = Path.Combine(Path.GetTempPath(), fileInfo.Name);
				Console.WriteLine($"Unable to write to file {fileInfo.FullName}, using logfile {file} instead");
			}

			_writer = new StreamWriter(file, true, Encoding.Unicode);
		}

		public static bool IsDirectoryWritable(string directory)
		{
			String randomFileName = Path.Combine(directory, Path.GetRandomFileName());
			try
			{
				using (FileStream fs = File.Create(randomFileName))
				{
					fs.WriteByte(50);
				}

				return true;
			}
			catch (Exception x) when (x is UnauthorizedAccessException || x is IOException)
			{
				return false;
			}
			finally
			{
				File.Delete(randomFileName);
			}
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			if (formatter != null && IsEnabled(logLevel))
			{
				string msg = formatter(state, exception);
				string formattedTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffff");
				string formatted = FormattableString.Invariant($"\"{formattedTime}\";\"{logLevel}\";\"{_loggerName}\";\"{msg}\";\"{exception}\"");
				// This method is not thread safe
				lock (_writer)
				{
					_writer.WriteLine(formatted);
					_writer.Flush();
				}
			}
		}

		public void Flush()
		{
			lock (_writer)
			{
				_writer.Flush();
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
			lock (_writer)
			{
				_writer.Dispose();
			}
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
