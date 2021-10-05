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
	}
}
