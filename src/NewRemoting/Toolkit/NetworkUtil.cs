using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	public static class NetworkUtil
	{
		public static bool TryGetIpAddressForHostName(string hostName, out IPAddress ipAddress)
		{
			if (!IPAddress.TryParse(hostName, out ipAddress))
			{
				IPHostEntry hostEntry = Dns.GetHostEntry(hostName);
				ipAddress = IPAddress.None;
				if (hostEntry != null && hostEntry.AddressList.Length > 0)
				{
					ipAddress = hostEntry.AddressList[0];
					return true;
				}

				return false;
			}

			return true;
		}

		/// <summary>
		/// Gets the list of IP addresses of the current computer
		/// </summary>
		/// <returns></returns>
		public static IPAddress[] LocalIpAddresses()
		{
			var host = Dns.GetHostEntry(Dns.GetHostName());
			return host.AddressList;
		}

		public static IPAddress GetLocalIp()
		{
			IPHostEntry iphostentry = Dns.GetHostEntry(Dns.GetHostName());
			// Enumerate IP addresses
			IPAddress localIp = null;
			foreach (IPAddress ip in iphostentry.AddressList)
			{
				if (ip.AddressFamily == AddressFamily.InterNetwork)
				{
					localIp = ip;
					break;
				}
			}

			return localIp;
		}

		/// <summary>
		/// Checks if an address is local
		/// </summary>
		/// <param name="host">A computer name or an IP</param>
		public static bool IsLocalIpAddress(string host)
		{
			try
			{ // get host IP addresses
				IPAddress[] hostIps = Dns.GetHostAddresses(host);
				// get local IP addresses
				IPAddress[] localIps = Dns.GetHostAddresses(Dns.GetHostName());

				// test if any host IP equals to any local IP or to localhost
				foreach (IPAddress hostIp in hostIps)
				{
					// is localhost
					if (IPAddress.IsLoopback(hostIp))
					{
						return true;
					}

					// is local address
					foreach (IPAddress localIp in localIps)
					{
						if (hostIp.Equals(localIp))
						{
							return true;
						}
					}
				}
			}
			catch (Exception x) when (!(x is NullReferenceException))
			{
			}

			return false;
		}
	}
}
