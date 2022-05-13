using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
		private FileUploadCandidate _uploadCandidate;

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

		public Version ClrVersion => Environment.Version;

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
		/// Checks if file exists on remote side with given <paramref name="hash"/> and <paramref name="relativePath"/> and prepares file upload.
		/// This should not be used if the server process lives on the same computer as the client.
		/// Use this function before calling <see cref="UploadFileData(byte[], int, int)"/> and <see cref="FinishFileUpload"/>.
		/// </summary>
		/// <param name="relativePath">The path where the file is to be placed, relative to the current executing directory</param>
		/// <param name="hash">The hash code of the file which is used to determine if the file is already present on the remote side</param>
		/// <returns>true if file is not present on remote system and upload procedure can be started</returns>
		public virtual bool PrepareFileUpload(string relativePath, byte[] hash)
		{
			// Create destination path for this system. We use current assembly path as root
			var dest = Path.Combine(_root.FullName, relativePath);
			var dir = new DirectoryInfo(Path.GetDirectoryName(dest));

			if (!dir.Exists)
			{
				dir.Create();
			}

			var fi = new FileInfo(dest);
			_uploadedFiles.Add(fi); // files that are already present and files that need to be transfered count as uploaded
			if (fi.Exists)
			{
				byte[] hashLocal = _fileHashCalculator.CalculateFastHashFromFile(fi.FullName);
				if (hashLocal.SequenceEqual(hash))
				{
					// no upload needed, file is already up to date!
					return false;
				}
			}

			_uploadCandidate = new FileUploadCandidate(fi);
			return true;
		}

		/// <summary>
		/// Writes the content of <paramref name="fileContent"/> to the file on the remote side.
		/// This should not be used if the server process lives on the same computer as the client.
		/// Call <see cref="PrepareFileUpload(string, byte[])"/> before this.
		/// </summary>
		/// <param name="fileContent">The content of the file.</param>
		/// <param name="count">The amount of bytes to write.</param>
		/// <exception cref="InvalidOperationException"></exception>
		public virtual void UploadFileData(byte[] fileContent, int count)
		{
			if (_uploadCandidate == null)
			{
				throw new InvalidOperationException($"{nameof(RemoteServerService)}: {nameof(PrepareFileUpload)} needs to be called before {nameof(UploadFileData)}");
			}

			_uploadCandidate.WriteToFile(fileContent, count);
		}

		/// <summary>
		/// Finishes writing to remote file.
		/// This should not be used if the server process lives on the same computer as the client.
		/// Call <see cref="PrepareFileUpload(string, byte[])"/> before this.
		/// </summary>
		/// <exception cref="InvalidOperationException"></exception>
		public virtual void FinishFileUpload()
		{
			if (_uploadCandidate == null)
			{
				throw new InvalidOperationException($"{nameof(RemoteServerService)}: {nameof(PrepareFileUpload)} needs to be called before {nameof(FinishFileUpload)}");
			}

			_uploadCandidate.Finish();
			FileInfo fileInfo = new FileInfo(_uploadCandidate.FullPath);
			_logger.LogInformation($"Finished upload of {fileInfo.Name} ({fileInfo.Length} bytes)");
			_uploadCandidate = null;
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

		public void PerformGc()
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.WaitForFullGCComplete();
			_server.PerformGc();
		}

		/// <summary>
		/// Helper class for <see cref="PrepareFileUpload(string, byte[])"/>, <see cref="UploadFileData(byte[], int, int)"/> and <see cref="FinishFileUpload"/>
		/// </summary>
		private class FileUploadCandidate
		{
			private readonly FileStream _fileStream;
			public FileUploadCandidate(FileInfo file)
			{
				FullPath = file.FullName;
				_fileStream = file.OpenWrite();
			}

			public string FullPath
			{
				get;
			}

			public void WriteToFile(byte[] fileContent, int count)
			{
				_fileStream.Write(fileContent, 0, count);
			}

			public void Finish()
			{
				_fileStream.Flush();
				_fileStream.Dispose();
			}
		}
	}
}
