using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NewRemoting
{
	/// <summary>
	/// Helper services for the remoting infrastructure, including copying files to a remote host.
	/// </summary>
	/// <remarks>Note: Do not seal this class!</remarks>
	public class RemoteServerService : MarshalByRefObject, IRemoteServerService
	{
		private readonly DirectoryInfo _root;
		private readonly List<FileInfo> _existingFiles;
		private readonly Server _server;
		private readonly FileHashCalculator _fileHashCalculator;
		private readonly ILogger _logger;
		private List<FileInfo> _uploadedFiles;

		internal RemoteServerService(Server server, ILogger logger)
			: this(server, new FileHashCalculator(), logger)
		{
		}

		internal RemoteServerService(Server server, FileHashCalculator fileHashCalculator, ILogger logger)
		{
			_server = server;
			_fileHashCalculator = fileHashCalculator ?? throw new ArgumentNullException(nameof(fileHashCalculator));
			_logger = logger;
			_root = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
			_existingFiles = ScanExistingFiles(_root, new List<FileInfo>());
			_uploadedFiles = new List<FileInfo>();
		}

		#region Unit Test
		internal List<FileInfo> UploadedFiles
		{
			get => _uploadedFiles;
			set => _uploadedFiles = value;
		}
		#endregion

		/// <summary>
		/// Returns true if this server is the only server instance running on this system.
		/// If this returns false, this may indicate that there's a dangling remote process potentially causing
		/// problems (unless it's expected to have multiple servers running on the same machine)
		/// </summary>
		public virtual bool IsSingleRemoteServerInstance()
		{
			try
			{
				var otherprocesses = OtherRemoteServers();
				return !otherprocesses.Any();
			}
			catch (Win32Exception)
			{
				// It was apparently not possible to enumerate the processes, this may happen if
				// another process exists but doesn't belong to the same user.
				return false;
			}
		}

		/// <summary>
		/// Returns a list of other running server processes
		/// </summary>
		/// <returns>A list of server processes</returns>
		private static IEnumerable<Process> OtherRemoteServers()
		{
			var loaderWithoutExt = Path.GetFileNameWithoutExtension(Server.ServerExecutableName);
			var processes = Process.GetProcessesByName(loaderWithoutExt);
			var otherprocesses = processes.Where(x => x.HasExited == false && x.Id != Process.GetCurrentProcess().Id);
			return otherprocesses;
		}

		/// <summary>
		/// Copies the contents of the provided stream to a file on the server (where this instance lives).
		/// This should not be used if the server process lives on the same computer as the client.
		/// </summary>
		/// <param name="relativePath">The path where the file is to be placed, relative to the current executing directory</param>
		/// <param name="hash">The hash code of the file</param>
		/// <param name="content">The payload</param>
		public virtual void UploadFile(string relativePath, byte[] hash, Stream content)
		{
			// Create desination path for this system. We use current assembly path as root
			var dest = Path.Combine(_root.FullName, relativePath);
			var dir = new DirectoryInfo(Path.GetDirectoryName(dest));

			if (!dir.Exists)
			{
				dir.Create();
			}

			var fi = new FileInfo(dest);
			_uploadedFiles.Add(fi);

			if (fi.Exists)
			{
				byte[] hashLocal = _fileHashCalculator.CalculateFastHashFromFile(fi.FullName);
				if (hashLocal.SequenceEqual(hash))
				{
					// no upload needed, file is already up to date!
					return;
				}
			}

			using (var stream = fi.OpenWrite())
			{
				_logger.LogInformation("Uploading file to target: {0}", dest);
				content.CopyTo(stream);
			}
		}

		/// <summary>
		/// Terminates the upload procedure.
		/// All files that were not uploaded but exist locally will be deleted.
		/// </summary>
		public virtual void UploadFinished()
		{
			// Remove all entries that have been uploaded in this session
			_existingFiles.RemoveAll(x => _uploadedFiles.Any(y => x.FullName.Equals(y.FullName, StringComparison.OrdinalIgnoreCase)));
			// The remaining files in list can be deleted, they where never uploaded
			foreach (FileInfo fileInfo in _existingFiles)
			{
				try
				{
					fileInfo.Delete();
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					_logger.LogWarning("Cleanup partially failed, could not delete file:" + fileInfo.FullName);
				}
			}
		}

		private static List<FileInfo> ScanExistingFiles(DirectoryInfo root, List<FileInfo> files)
		{
			files.AddRange(root.GetFiles());

			foreach (DirectoryInfo directory in root.GetDirectories())
			{
				ScanExistingFiles(directory, files);
			}

			return files;
		}

		/// <summary>
		/// Returns true. If called remotely, this tests the connection.
		/// </summary>
		/// <returns>Always true</returns>
		public bool Ping()
		{
			return true;
		}

		public void TerminateRemoteServerService()
		{
			_server.Terminate(false);
		}

		public void RegisterServiceOnServer(Type typeToRegister, object instance)
		{
			ServiceContainer.AddService(typeToRegister, instance);
		}

		public void RemoveServiceFromServer(Type typeToUnregister)
		{
			ServiceContainer.RemoveService(typeToUnregister);
		}

		public object QueryServiceFromServer(Type typeToQuery)
		{
			return ServiceContainer.GetService(typeToQuery);
		}

		public T QueryServiceFromServer<T>()
		{
			return ServiceContainer.GetService<T>();
		}
	}
}
