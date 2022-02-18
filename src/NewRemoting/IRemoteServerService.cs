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
		/// Checks if file exists on remote side with given <paramref name="hash"/> and <paramref name="relativePath"/> and prepares file upload.
		/// This should not be used if the server process lives on the same computer as the client.
		/// Use this function before calling <see cref="UploadFileData(byte[], int, int)"/> and <see cref="FinishFileUpload"/>.
		/// </summary>
		/// <param name="relativePath">The path where the file is to be placed, relative to the current executing directory</param>
		/// <param name="hash">The hash code of the file which is used to determine if the file is already present on the remote side</param>
		/// <returns>true if file is not present on remote system and upload procedure can be started</returns>
		bool PrepareFileUpload(string relativePath, byte[] hash);

		/// <summary>
		/// Writes the content of <paramref name="fileContent"/> to the file on the remote side.
		/// This should not be used if the server process lives on the same computer as the client.
		/// Call <see cref="PrepareFileUpload(string, byte[])"/> before this.
		/// </summary>
		/// <param name="fileContent">The content of the file.</param>
		/// <param name="count">The amount of bytes to write.</param>
		/// <exception cref="InvalidOperationException"></exception>
		void UploadFileData(byte[] fileContent, int count);

		/// <summary>
		/// Finishes writing to remote file.
		/// This should not be used if the server process lives on the same computer as the client.
		/// Call <see cref="PrepareFileUpload(string, byte[])"/> before this.
		/// </summary>
		/// <exception cref="InvalidOperationException"></exception>
		void FinishFileUpload();

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
