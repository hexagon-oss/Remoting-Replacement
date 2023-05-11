using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using NewRemoting;

namespace RemotingServer
{
	public class CommandLineOptions
	{
		[Option('p', "port", Default = Client.DefaultNetworkPort, HelpText = "Network port to use")]
		public int? Port
		{
			get;
			set;
		}

		[Option('k', "kill", HelpText = "Kills own process if the connection ends")]
		public bool KillSelf
		{
			get;
			set;
		}

		[Option('v', "verbose", HelpText = "Print verbose output (to console and debug output)")]
		public bool Verbose
		{
			get;
			set;
		}

		[Option("localEndPoint", HelpText = "Add option to set a local endpoint for servers running on systems with multiple IPs")]
		public string LocalEndPoint
		{
			get;
			set;
		}

		[Option('l', "logfile", HelpText = "Writes log data to the given log file. Should only be used for debugging, as it has a significant performance impact. Cannot be used with -v.")]
		public string LogFile
		{
			get;
			set;
		}
	}
}
