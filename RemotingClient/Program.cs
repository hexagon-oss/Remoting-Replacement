using System;
using System.Diagnostics;
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
            MarshallableClass cls = client.CreateRemoteInstance<MarshallableClass>(typeof(MarshallableClass));
            int number = cls.GetSomeData();
            Console.WriteLine($"Server said the number is {number}!");
            int remotePs = cls.GetCurrentProcessId();
            Console.WriteLine($"Local Process: {Process.GetCurrentProcess().Id}, Remote Process: {remotePs}");
            Console.WriteLine($"2 + 5 = {cls.AddValues(2, 5)}");

            Console.WriteLine($"Remote process id (again): {cls.StringProcessId()}");

            ReferencedComponent component = cls.GetComponent();

            Console.WriteLine($"Component returns {component.SuperNumber()} and {component.SuperNumber()}");
            component.Data = 2;
            Console.WriteLine($"Now the data is {component.Data}");
        }
        
    }
}
