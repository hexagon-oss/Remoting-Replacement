using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using NewRemoting;
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
            using NewRemoting.RemotingClient client = new NewRemoting.RemotingClient("localhost", 23456);
            MarshallableClass cls = client.CreateRemoteInstance<MarshallableClass>(typeof(MarshallableClass));
            int number = cls.GetSomeData();
            Console.WriteLine($"Server said the number is {number}!");
            int remotePs = cls.GetCurrentProcessId();
            Console.WriteLine($"Local Process: {Process.GetCurrentProcess().Id}, Remote Process: {remotePs}");
            Console.WriteLine($"2 + 5 = {cls.AddValues(2, 5)}");

            ReferencedComponent component = cls.GetComponent();

            Console.WriteLine($"Component returns {component.SuperNumber()} and {component.SuperNumber()}");
            component.Data = 2;
            Console.WriteLine($"Now the data is {component.Data}");

            if (cls.TryParseInt("23", out int value))
            {
                Console.WriteLine($"The string 23 was converted to number {value}.");
            }

            int aValue = 4;
            cls.UpdateArgument(ref aValue);
            Console.WriteLine($"The return value should be 6, and it is {aValue}");

            IMarshallInterface interf = cls;
            Console.WriteLine($"Remote process id (again): {interf.StringProcessId()}");

            var cbi = new CallbackImpl();
            cls.RegisterCallback(cbi);
            cls.DoCallback();

            IMyComponentInterface myComponentInterface = cls.GetInterface<IMyComponentInterface>();

            Console.WriteLine("Remote process name is " + myComponentInterface.ProcessName());

            // myComponentInterface.TimeChanged += MyComponentInterfaceOnTimeChanged;

            var sinkInstance = new MyClassWithAnEventSink("Client");
            myComponentInterface.TimeChanged += sinkInstance.OnTimeChanged;

            myComponentInterface.StartTiming();

            Thread.Sleep(5000);
            IDisposable disposable = (IDisposable) myComponentInterface;
            disposable.Dispose();
        }

        public static void MyComponentInterfaceOnTimeChanged(DateTime obj)
        {
            Console.WriteLine($"It is now {obj.ToLongDateString()}");
        }

        internal sealed class MyClassWithAnEventSink
        {
            private string _testName;
            public MyClassWithAnEventSink(string name)
            {
                _testName = name;
            }

            public void OnTimeChanged(DateTime obj)
            {
                Console.WriteLine($"It really is {obj.ToLongTimeString()} on {_testName}");
            }
        }
    }
}
