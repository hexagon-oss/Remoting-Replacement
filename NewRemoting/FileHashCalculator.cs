using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Airborne.Generic.Remote.Loader
{
	public class FileHashCalculator : IDisposable
	{
		private readonly MD5CryptoServiceProvider m_md5;

		public FileHashCalculator()
		{
			m_md5 = new MD5CryptoServiceProvider();
		}

		/// <summary>
		/// Returns a hash code for a file. Files containing version data mainly use that instead of reading the whole file
		/// </summary>
		/// <param name="fileName">File to analyze</param>
		/// <returns>A byte[] with the hashcode</returns>
		public virtual byte[] CalculateFastHashFromFile(string fileName)
		{
			using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);

			if (stream.Length < 64 * 1024)
			{
				return m_md5.ComputeHash(stream);
			}

			FileVersionInfo info = FileVersionInfo.GetVersionInfo(fileName);
			bool isExecutable = FileIsExecutable(stream);
			stream.Position = 0;
			// if the file is executable or has a version attached, we use that for comparison.
			if (isExecutable || !string.IsNullOrWhiteSpace(info.FileVersion))
			{
				// Create a combined stream of the file version and the file header
				MemoryStream dataToAnalyze = new MemoryStream();
				string versionText = FileVersionToString(info);
				byte[] infoBytes = Encoding.Unicode.GetBytes(versionText);
				dataToAnalyze.Write(infoBytes, 0, infoBytes.Length);
				byte[] firstBlock = new byte[1024];
				stream.Read(firstBlock, 0, firstBlock.Length);
				dataToAnalyze.Write(firstBlock, 0, firstBlock.Length);
				dataToAnalyze.Position = 0;

				return m_md5.ComputeHash(dataToAnalyze);
			}

			// It's a large, unknown file type. We have to go the full way
			return m_md5.ComputeHash(stream);
		}

		/// <summary>
		/// Returns the file this file depends on, if any. If the other file needs updating, this one needs, too.
		/// This can be used i.e. to keep pdbs in sync with their executables. Calculating the hash of pdbs on the
		/// other hand is expensive, because they have no version info and are typically large.
		/// </summary>
		/// <param name="fileName">File to check</param>
		/// <returns>The file this depends on or null if no such file is known. Also returns null if the main file
		/// does not exist</returns>
		public virtual string GetFileThisDependsOn(string fileName)
		{
			FileInfo fi = new FileInfo(fileName);
			if (string.Compare(fi.Extension, "PDB", StringComparison.OrdinalIgnoreCase) == 0)
			{
				string fileToCheck = Path.GetFileNameWithoutExtension(fileName) + ".dll";
				if (File.Exists(fileToCheck))
				{
					return fileToCheck;
				}

				fileToCheck = Path.GetFileNameWithoutExtension(fileName) + ".exe";
				if (File.Exists(fileToCheck))
				{
					return fileToCheck;
				}

				return null;
			}

			return null;
		}

		private static string FileVersionToString(FileVersionInfo info)
		{
			// The default "ToString()" implementation includes the path of the full file, which we do not want to compare
			StringBuilder b = new StringBuilder();
			b.AppendLine("FileVersion:" + info.FileVersion);
			b.AppendLine("Description: " + info.FileDescription);
			b.AppendLine("OriginalName: " + info.OriginalFilename);
			b.AppendLine("ProductName: " + info.ProductName);
			b.AppendLine("ProductVersion: " + info.ProductVersion);
			b.AppendLine("Comments" + info.Comments);
			return b.ToString();
		}

		private static bool FileIsExecutable(Stream stream)
		{
			if (stream.Length > 1024)
			{
				int first = stream.ReadByte();
				int second = stream.ReadByte();

				// Magic number: EXE header
				if (first != 'M' && second != 'Z')
				{
					return false;
				}

				stream.Position = 0x80;
				first = stream.ReadByte();
				second = stream.ReadByte();
				// Second magic number
				if (first != 'P' && second != 'E')
				{
					return false;
				}

				return true;
			}

			return false;
		}

		protected virtual void Dispose(bool disposing)
		{
			m_md5.Dispose();
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
	}
}
