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
	}
}
