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
		private readonly DirectoryInfo m_root;
		private readonly List<FileInfo> m_existingFiles;
		private readonly FileHashCalculator m_fileHashCalculator;
		private List<FileInfo> m_uploadedFiles;

		public RemoteServerService()
			: this(new FileHashCalculator())
		{
		}

		public RemoteServerService(FileHashCalculator fileHashCalculator)
		{
			m_fileHashCalculator = fileHashCalculator ?? throw new ArgumentNullException(nameof(fileHashCalculator));
			m_root = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
			m_existingFiles = ScanExistingFiles(m_root, new List<FileInfo>());
			m_uploadedFiles = new List<FileInfo>();
		}

		#region Unit Test
		[Obsolete("Unit test only")]
		internal List<FileInfo> UploadedFiles
		{
			get => m_uploadedFiles;
			set => m_uploadedFiles = value;
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
			var dest = Path.Combine(m_root.FullName, relativePath);
			var dir = new DirectoryInfo(Path.GetDirectoryName(dest));

			if (!dir.Exists)
			{
				dir.Create();
			}

			var fi = new FileInfo(dest);
			m_uploadedFiles.Add(fi);

			if (fi.Exists)
			{
				byte[] hashLocal = m_fileHashCalculator.CalculateFastHashFromFile(fi.FullName);
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
			m_existingFiles.RemoveAll(x => m_uploadedFiles.Any(y => x.FullName.Equals(y.FullName, StringComparison.OrdinalIgnoreCase)));
			// The remaining files in list can be deleted, they where never uploaded
			foreach (FileInfo fileInfo in m_existingFiles)
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
	}
}
