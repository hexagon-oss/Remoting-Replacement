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

				var allKeys = ConfigurationManager.AppSettings.AllKeys;
				string certificate = null;
				string certPwd = null;

				if (allKeys.Contains("CertificateFileName"))
				{
					var cert = ConfigurationManager.AppSettings.Get("CertificateFileName");
					if (!cert.IsNullOrEmpty())
					{
						certificate = cert;
					}
				}

				if (allKeys.Contains("CertificatePassword"))
				{
					certPwd = ConfigurationManager.AppSettings.Get("CertificatePassword");
				}

				if (!certificate.IsNullOrEmpty())
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
			}
		}
	}
}
