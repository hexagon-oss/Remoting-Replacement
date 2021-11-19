using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	public interface IRemoteServerService
	{
		/// <summary>
		/// Upload a file to the remote systems working directory.
		/// </summary>
		void UploadFile(string relativePath, byte[] hash, Stream content);

		/// <summary>
		/// Signal remote system that upload is finished.
		/// Remote system will cleanup unused files and folders.
		/// </summary>
		void UploadFinished();

		/// <summary>
		/// Pings the remote server
		/// </summary>
		/// <returns>True</returns>
		bool Ping();

		/// <summary>
		/// This terminates the remote service.
		/// Typically this is not used directly as the client shuts down first. It may be helpful to shut down
		/// a complex system of multiple server and client instances.
		/// </summary>
		public void TerminateRemoteServerService();

		/// <summary>
		/// Registers the given instance as service on the server side
		/// </summary>
		/// <param name="typeToRegister">The type under which the instance should be registered. This should preferably be an interface</param>
		/// <param name="instance">The instance to register</param>
		/// <exception cref="InvalidOperationException">The object being registered does not inherit from <see cref="MarshalByRefObject"/></exception>
		void RegisterServiceOnServer(Type typeToRegister, object instance);

		/// <summary>
		/// Removes the given interface from the service registry
		/// </summary>
		/// <param name="typeToUnregister">The type to remove</param>
		void RemoveServiceFromServer(Type typeToUnregister);

		/// <summary>
		/// Returns a registered instance of the given type
		/// </summary>
		/// <param name="typeToQuery">The type to query</param>
		/// <returns>An instance of the given type (as proxy) or null if no such instance was registered</returns>
		object QueryServiceFromServer(Type typeToQuery);

		/// <summary>
		/// Returns a registered instance of the given type
		/// </summary>
		/// <typeparam name="T">The type to query</typeparam>
		/// <returns>An instance of the given type (as proxy) or null if no such instance was registered</returns>
		T QueryServiceFromServer<T>();
	}
}
