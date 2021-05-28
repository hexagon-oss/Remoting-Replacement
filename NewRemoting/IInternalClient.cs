using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	internal interface IInternalClient
	{
		/// <summary>
		/// The lock for the underling communication instance.
		/// This is dangerous to expose, but the interface is internal, so this should be fine.
		/// </summary>
		object CommunicationLinkLock
		{
			get;
		}

		void AddKnownRemoteInstance(object obj, string objectId);

		bool TryGetRemoteInstance(object obj, out string objectId);

		object GetLocalInstanceFromReference(string objectId);

		bool TryGetLocalInstanceFromReference(string objectId, out object obj);

		string GetIdForLocalObject(object obj, out bool isNew);
	}
}
