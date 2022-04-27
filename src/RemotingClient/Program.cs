using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading;
using CommandLine;
using Microsoft.Extensions.Logging;
using NewRemoting;
using SampleServerClasses;

namespace RemotingClient
{
	internal class Program
	{
		public static void Main(string[] args)
		{
			Console.WriteLine("Hello World of clients!");
			var parsed = Parser.Default.ParseArguments<CommandLineOptions>(args);
			CommandLineOptions options = null;
			parsed.WithParsed(r => options = r);
			string certificate = options?.Certificate;
			if (args.Any(x => x == "--debug"))
			{
				Console.WriteLine("Waiting for debugger...");
				while (!Console.KeyAvailable && !Debugger.IsAttached)
				{
					Thread.Sleep(20);
				}
			}

			string ip = "localhost";

			if (!string.IsNullOrEmpty(options.Ip))
			{
				ip = options.Ip;
			}

			DoSomeRemoting(certificate, ip);
		}

		public static void DoSomeRemoting(string certificate, string ip)
		{
			using var client = GetClient(certificate, ip);
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

			var sinkInstance = new MyClassWithAnEventSink();
			myComponentInterface.TimeChanged += sinkInstance.OnTimeChanged;

			myComponentInterface.StartTiming();

			Thread.Sleep(20000);
			IDisposable disposable = (IDisposable)myComponentInterface;
			disposable.Dispose();

			var arguments = new ConstructorArgument(new ReferencedComponent());
			var service = client.CreateRemoteInstance<ServiceClass>(arguments);

			Console.WriteLine($"The reply should be round-tripped to the client: {service.DoSomething()}");

			var bios = client.CreateRemoteInstance<CheckBiosVersion>();

			string[] versions = bios.GetBiosVersions();
			Console.WriteLine($"Server bios versions are: {string.Join(", ", versions)}.");

			// Wait until the GC kicks in
			GC.Collect();
			GC.WaitForFullGCComplete();
			GC.WaitForPendingFinalizers();
			Thread.Sleep(20000);
			client.ForceGc();
			Thread.Sleep(1000);
			var moreVersions = bios.GetBiosVersions();
			if (moreVersions[0] != versions[0])
			{
				Console.WriteLine("The bios version has changed unexpectedly!?!?");
			}

			Console.WriteLine("Getting remote server service...");
			var serverService = client.RequestRemoteInstance<IRemoteServerService>();
			Stopwatch sw = Stopwatch.StartNew();
			serverService.Ping();
			Console.WriteLine($"Pinging took {sw.Elapsed.TotalMilliseconds}ms");

			try
			{
				cls.CallerError(new UnserializableObject());
			}
			catch (SerializationException x)
			{
				Console.WriteLine("This exception is expected: " + x.Message);
			}

			try
			{
				var obj = cls.ServerError();
				if (obj != null)
				{
					Console.WriteLine("This shouldn't happen");
				}
			}
			catch (SerializationException x)
			{
				Console.WriteLine("This exception is also expected: " + x.Message);
			}

			/*
			try
			{
				cls.MaybeThrowException(0);
			}
			catch (DivideByZeroException x)
			{
				Console.WriteLine("Caught " + x);
			}
			*/

			SerializableClassWithMarshallableMembers sc = new SerializableClassWithMarshallableMembers(1, new ReferencedComponent() { Data = 10 });

			int reply = cls.UseMixedArgument(sc);

			Console.WriteLine($"This result should be 10. It is {reply}");

			reply = sc.CallbackViaComponent();

			Console.WriteLine($"This result should still be 10. It is {reply}");

			var sc2 = sc.ReturnSelfToCaller();

			Console.WriteLine($"This result is hopefully true: {ReferenceEquals(sc, sc2)}");

			Console.WriteLine("Shutting down server, then client");
			client.ShutdownServer();
		}

		public static void MyComponentInterfaceOnTimeChanged(DateTime obj)
		{
			Console.WriteLine($"It is now {obj.ToLongDateString()}");
		}

		private static Client GetClient(string certificate, string ip)
		{
			int i = 5;
			while (true)
			{
				try
				{
					Client client = new Client(ip, Client.DefaultNetworkPort, certificate, new SimpleLogFileWriter("ClientLog.log", "ClientLog", LogLevel.Trace));
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

	internal class CommandLineOptions
	{
		[Option('c', "certificate", HelpText = "full filename of the certificate")]
		public string Certificate
		{
			get;
			set;
		}

		[Option('i', "ip", HelpText = "ip address")]
		public string Ip
		{
			get;
			set;
		}
	}
}
