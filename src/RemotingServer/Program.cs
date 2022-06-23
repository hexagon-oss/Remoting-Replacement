using System;
using System.Configuration;
using System.IO;
using System.Linq;
using Castle.Core.Internal;
using CommandLine;
using Microsoft.Extensions.Logging;
using NewRemoting;

namespace RemotingServer
{
	internal class Program
	{
		public static void Main(string[] args)
		{
			Console.WriteLine("Hello World of Remoting Servers!");
			StartServer(args);
			Console.WriteLine("Server gracefully exiting");
		}

		public static void StartServer(string[] args)
		{
			var parsed = Parser.Default.ParseArguments<CommandLineOptions>(args);
			int port = Client.DefaultNetworkPort;
			CommandLineOptions options = null;
			parsed.WithParsed(r => options = r);
			if (options != null)
			{
				if (options.Port.HasValue)
				{
					port = options.Port.Value;
				}

				ILogger logger = null;
				if (options.Verbose)
				{
					logger = new ConsoleAndDebugLogger("RemotingServer");
				}

				if (!string.IsNullOrWhiteSpace(options.LogFile))
				{
					// logfile argument is expected to be without root path (as it is called remotely without knowledge of local paths)
					logger = new SimpleLogFileWriter(Path.Combine(Path.GetTempPath(), options.LogFile), "ServerLog", LogLevel.Trace);
				}

				var allKeys = ConfigurationManager.AppSettings.AllKeys;
				string certificate = null;
				string certPwd = null;

				if (allKeys.Contains("CertificateFileName"))
				{
					var cert = ConfigurationManager.AppSettings.Get("CertificateFileName");
					if (!string.IsNullOrEmpty(cert))
					{
						certificate = cert;
					}
				}

				if (!string.IsNullOrEmpty(certificate))
				{
					logger?.LogInformation("Certificate provided to application");
				}
				else
				{
					logger?.LogInformation("Certificate not provided to application.");
				}

				if (allKeys.Contains("CertificatePassword"))
				{
					certPwd = ConfigurationManager.AppSettings.Get("CertificatePassword");
					if (!string.IsNullOrEmpty(certPwd))
					{
						logger?.LogInformation("password provided to application.");
					}
				}

				if (!string.IsNullOrEmpty(certificate))
				{
					if (!File.Exists(certificate))
					{
						Console.WriteLine($"Certificate {certificate} does not exist");
						return;
					}
				}

				var server = new Server(port, certificate, certPwd, logger);
				if (options.KillSelf)
				{
					server.KillProcessWhenChannelDisconnected = true;
				}

				// Temporary (Need to move the server creation logic to the library)
				server.KillProcessWhenChannelDisconnected = true;

				server.StartListening();
				server.WaitForTermination();
				server.Terminate(false);
				GC.KeepAlive(server);
				if (logger is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}
		}
	}
}
