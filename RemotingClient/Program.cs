using System;
using System.Net.Sockets;
using RemotingServer;

namespace RemotingClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World of clients!");
            DoSomeRemoting();
        }

        public static void DoSomeRemoting()
        {
            RemotingClient client = new RemotingClient("localhost", 23456);
            MarshallableClass cls = (MarshallableClass)client.CreateRemoteInstance(typeof(MarshallableClass));
            int number = cls.GetRandomNumber();
            Console.WriteLine($"Server said the random number is {number}!");
        }
        
    }
}
