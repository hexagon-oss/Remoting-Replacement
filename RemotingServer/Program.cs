using System;
using Castle.DynamicProxy;

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
            var server = new NewRemoting.RemotingServer(23456);
            server.StartListening();
            server.WaitForTermination();
            server.Terminate();
            GC.KeepAlive(server);
        }
    }
}
