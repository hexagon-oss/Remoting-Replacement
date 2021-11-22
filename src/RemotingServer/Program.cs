using System;
using System.Diagnostics;
using System.Threading;
using Castle.DynamicProxy;
using NewRemoting;
using CommandLine;
using Microsoft.Extensions.Logging;

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

				var server = new Server(port, logger);
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
