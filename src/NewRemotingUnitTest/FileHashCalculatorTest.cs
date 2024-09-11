using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	public class FileHashCalculatorTest
	{
		[Test]
		[Explicit("Just a performance test")]
		public void CalculateAllHashes()
		{
			FileHashCalculator streamHashCalculator = new();
			Stopwatch sw = Stopwatch.StartNew();
			FileInfo ownFile = new FileInfo(Assembly.GetExecutingAssembly().Location);
			DirectoryInfo di = ownFile.Directory;
			Dictionary<FileInfo, byte[]> hashCodes = new();
			foreach (var file in di.GetFiles("*.*", SearchOption.AllDirectories))
			{
				byte[] hash = streamHashCalculator.CalculateFastHashFromFile(file.FullName);
				Assert.That(hash.Length, Is.EqualTo(16));
				Assert.That(hash.GetHashCode(), Is.Not.EqualTo(0));
				hashCodes.Add(file, hash);
			}

			Console.WriteLine($"Calculating all hashes in directory took {sw.Elapsed.TotalMilliseconds} ms");
			Console.WriteLine($"Number of files: {hashCodes.Count}");
		}

		[Test]
		public void HashOfSameFileIsEqual()
		{
			FileHashCalculator hashCalculator = new();
			FileInfo ownFile = new FileInfo(Assembly.GetExecutingAssembly().Location);
			DirectoryInfo di = ownFile.Directory;
			// These files are actually the same
			string firstFile = Path.Combine(di.FullName, "NewRemoting.dll");
			string secondFile = Path.Combine(di.FullName, "NewRemoting.dll");

			byte[] firstHash = hashCalculator.CalculateFastHashFromFile(firstFile);
			byte[] secondHash = hashCalculator.CalculateFastHashFromFile(secondFile);
			Assert.That(firstHash, Is.EqualTo(secondHash));
		}

		[Test]
		public void HashOfDifferentFileIsNotEqual()
		{
			FileHashCalculator hashCalculator = new();
			FileInfo ownFile = new FileInfo(Assembly.GetExecutingAssembly().Location);
			DirectoryInfo di = ownFile.Directory;
			string firstFile = Path.Combine(di.FullName, "NewRemoting.dll");
			string secondFile = Path.Combine(di.FullName, "NewRemotingUnitTest.dll");

			byte[] firstHash = hashCalculator.CalculateFastHashFromFile(firstFile);
			byte[] secondHash = hashCalculator.CalculateFastHashFromFile(secondFile);
			Assert.That(firstHash, Is.EqualTo(secondHash));
		}
	}
}
