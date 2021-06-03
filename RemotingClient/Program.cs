﻿using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using NewRemoting;
using RemotingServer;
using SampleServerClasses;

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
			using var client = GetClient();
			MarshallableClass cls = client.CreateRemoteInstance<MarshallableClass>();
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

			var sinkInstance = new MyClassWithAnEventSink();
			myComponentInterface.TimeChanged += sinkInstance.OnTimeChanged;

			myComponentInterface.StartTiming();

			Thread.Sleep(5000);
			IDisposable disposable = (IDisposable)myComponentInterface;
			disposable.Dispose();

			var arguments = new ConstructorArgument(new ReferencedComponent());
			var service = client.CreateRemoteInstance<ServiceClass>(arguments);

			Console.WriteLine($"The reply should be round-tripped to the client: {service.DoSomething()}");

			client.ShutdownServer();
		}

		public static void MyComponentInterfaceOnTimeChanged(DateTime obj)
		{
			Console.WriteLine($"It is now {obj.ToLongDateString()}");
		}

		private static Client GetClient()
		{
			int i = 5;
			while (true)
			{
				try
				{
					Client client = new NewRemoting.Client("localhost", Client.DefaultNetworkPort);
					return client;
				}
				catch (SocketException x)
				{
					Console.WriteLine($"Exception connecting to server: {x}");
					i--;
					Thread.Sleep(500);
					if (i <= 0)
					{
						throw;
					}
				}
			}
		}

		internal sealed class MyClassWithAnEventSink
		{
			public void OnTimeChanged(DateTime obj, string where)
			{
				Console.WriteLine($"It really is {obj.ToLongTimeString()} on {where}");
			}
		}
	}
}
