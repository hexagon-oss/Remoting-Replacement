using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
    public class RemoteServerService : MarshalByRefObject, IRemoteServerService
    {
        private readonly MD5CryptoServiceProvider _cryptographyFactory;
        private readonly DirectoryInfo _root;
        private List<FileInfo> _uploadedFiles;
        private List<FileInfo> _existingFiles;
        
        public RemoteServerService()
        {
            _cryptographyFactory = new MD5CryptoServiceProvider();
            _uploadedFiles = new List<FileInfo>();
            _root = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            _existingFiles = ScanExistingFiles(_root, new List<FileInfo>());
        }

        public void UploadFile(string relativePath, byte[] hash, Stream content)
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
                using (var stream = fi.OpenRead())
                {
                    // Calculate hash from existing file and compare if an upload is needed
                    var hashLocal = _cryptographyFactory.ComputeHash(stream);
                    if (hashLocal.SequenceEqual(hash))
                    {
                        // no upload needed, file is already up to date!
                        return;
                    }
                    
                }
            }

            using (var stream = fi.OpenWrite())
            {
                Console.WriteLine("Upload file to target: {0}", dest);
                content.CopyTo(stream);
            }
		}

        public void UploadFinished()
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
    }
}
