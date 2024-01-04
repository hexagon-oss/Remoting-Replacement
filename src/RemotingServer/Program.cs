using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Castle.Core.Internal;
using CommandLine;
using Microsoft.Extensions.Logging;
using NewRemoting;

namespace RemotingServer
{
	internal class Program
	{
		public static int Main(string[] args)
		{
			Console.WriteLine("Hello World of Remoting Servers!");
			if (!StartServer(args))
			{
				Console.WriteLine("Server failed to start.");
				return -1;
			}

			Console.WriteLine("Server gracefully exiting");
			return 0;
		}

		public static bool StartServer(string[] args)
		{
			ILogger logger = null;
			try
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

					if (!string.IsNullOrWhiteSpace(options.LogFile))
				{
					try
					{
						var info = new FileInfo(options.LogFile);
						info.Directory?.Create();
						logger = new SimpleLogFileWriter(options.LogFile, "ServerLog", options.Verbose ? LogLevel.Trace : LogLevel.Information);
					}
					catch (IOException)
					{
						logger = new SimpleLogFileWriter(Path.Combine(Path.GetTempPath(), options.LogFile), "ServerLog", options.Verbose ? LogLevel.Trace : LogLevel.Information);
					}
				}
					else if (options.Verbose)
					{
						logger = new ConsoleAndDebugLogger("RemotingServer");
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
							return false;
						}
					}

					var server = new Server(port, new AuthenticationInformation(certificate, certPwd), logger);
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

				return true;
			}
			catch (SocketException e)
			{
				Console.WriteLine(e);
				logger?.LogError(e, "start server failed");
				return false;
			}
			finally
			{
				logger?.LogInformation("end of program");
			}
		}
	}
}
