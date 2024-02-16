using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NewRemoting
{
	public sealed class ConnectionSettings
	{
		public ConnectionSettings()
		{
			InterfaceOnlyClient = false;
			ClientTypeFilter = t => t.IsPublic && t.IsInterface;
		}

		internal ConnectionSettings(bool interfaceOnlyClient, Func<Type, bool> clientTypeFilter)
		: this()
		{
			InterfaceOnlyClient = interfaceOnlyClient;
			if (clientTypeFilter != null)
			{
				ClientTypeFilter = clientTypeFilter;
			}
		}

		/// <summary>
		/// True if this client cannot access the implementation assemblies. Restricts the client
		/// from using certain ways of casting the provided remote objects
		/// </summary>
		public bool InterfaceOnlyClient
		{
			get;
			init;
		}

		/// <summary>
		/// Optional logging sink (for diagnostic messages)
		/// </summary>
		public ILogger InstanceManagerLogger
		{
			get;
			init;
		}

		/// <summary>
		/// Logger to trace this very client
		/// </summary>
		public ILogger ConnectionLogger
		{
			get;
			init;
		}

		/// <summary>
		/// Used on the server side only: Filter for interfaces to send to the client, only used if <see cref="InterfaceOnlyClient"/> is true.
		/// </summary>
		internal Func<Type, bool> ClientTypeFilter
		{
			get;
		}
	}
}
