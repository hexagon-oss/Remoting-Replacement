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
        }

        public static void StartServer()
        {
            var server = new RemotingServer(23456);
            server.StartListening();
            Console.ReadLine();
            server.Terminate();
            GC.KeepAlive(server);
        }

        //public static void DoStartProxying()
        //{
        //    var builder = new DefaultProxyBuilder();
        //    var proxy = new ProxyGenerator(builder);
        //    MarshallableClass instance = (MarshallableClass)proxy.CreateClassProxy(typeof(MarshallableClass), ProxyGenerationOptions.Default, new ClientSideInterceptor());
        //    int number = instance.GetRandomNumber();
        //    Console.WriteLine($"Got a number: {number}");
        //}
    }
}
