using System;
using Castle.DynamicProxy;
using NewRemoting;

namespace RemotingServer
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Hello World of Remoting Servers!");
			StartServer();
			Console.WriteLine("Server gracefully exiting");
		}

		public static void StartServer()
		{
			var server = new Server(Client.DefaultNetworkPort);
			server.StartListening();
			server.WaitForTermination();
			server.Terminate();
			GC.KeepAlive(server);
		}
	}
}
