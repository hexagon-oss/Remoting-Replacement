using System;
using System.Diagnostics;
using System.Threading;
using Castle.DynamicProxy;
using NewRemoting;
using CommandLine;

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
			if (parsed.Value.Port.HasValue)
			{
				port = parsed.Value.Port.Value;
			}

			var server = new Server(port);
			if (parsed.Value.KillSelf)
			{
				server.KillProcessWhenChannelDisconnected = true;
			}

			// Temporary (Need to move the server creation logic to the library)
			server.KillProcessWhenChannelDisconnected = true;

			server.StartListening();
			server.WaitForTermination();
			server.Terminate();
			GC.KeepAlive(server);
		}
	}
}
