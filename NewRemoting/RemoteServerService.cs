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
		private readonly FileHashCalculator _fileHashCalculator;
		private List<FileInfo> _uploadedFiles;

		public RemoteServerService()
			: this(new FileHashCalculator())
		{
		}

		public RemoteServerService(FileHashCalculator fileHashCalculator)
		{
			_fileHashCalculator = fileHashCalculator ?? throw new ArgumentNullException(nameof(fileHashCalculator));
			_root = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
			_existingFiles = ScanExistingFiles(_root, new List<FileInfo>());
			_uploadedFiles = new List<FileInfo>();
		}

		#region Unit Test
		[Obsolete("Unit test only")]
		internal List<FileInfo> UploadedFiles
		{
			get => _uploadedFiles;
			set => _uploadedFiles = value;
		}
		#endregion

		public virtual bool IsSingleRemoteLoaderInstance()
		{
			try
			{
				var otherprocesses = OtherRemoteLoaders();
				return !otherprocesses.Any();
			}
			catch (Win32Exception)
			{
				// It was apparently not possible to enumerate the processes, this may happen if
				// another process exists but doesn't belong to the same user.
				return false;
			}
		}

		private static IEnumerable<Process> OtherRemoteLoaders()
		{
			var loaderWithoutExt = Path.GetFileNameWithoutExtension(Server.ServerExecutableName);
			var processes = Process.GetProcessesByName(loaderWithoutExt);
			var otherprocesses = processes.Where(x => x.HasExited == false && x.Id != Process.GetCurrentProcess().Id);
			return otherprocesses;
		}

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
				Console.WriteLine("Upload file to target: {0}", dest);
				content.CopyTo(stream);
			}
		}

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
					Console.WriteLine("Cleanup partially failed, could not delete file:" + fileInfo.FullName);
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

		public bool Ping()
		{
			return true;
		}
	}
}
