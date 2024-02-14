using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	public class ServerTest
	{
		[Test]
		public void GetBestRuntimeDll1()
		{
			string[] input = new string[]
			{
				"runtimes\\win\\lib\\netcoreapp2.0\\System.Management.dll",
				"runtimes\\win\\lib\\netcoreapp3.1\\System.Management.dll"
			};

			string path = null;
			Server.GetBestRuntimeDll(input, ref path);

			Assert.That(path, Is.EqualTo(input[1]));
		}

		[Test]
		public void GetBestRuntimeDll2()
		{
			string[] input = new string[]
			{
				"runtimes\\win\\lib\\netstandard2.0\\System.Management.dll",
				"runtimes\\win\\lib\\net5.0\\System.Management.dll"
			};

			string path = null;
			Server.GetBestRuntimeDll(input, ref path);

			Assert.AreEqual(input[1], path);
		}

		[Test]
		public void GetBestRuntimeDll3()
		{
			string[] input = new string[]
			{
				"runtimes\\win\\lib\\netstandard2.1\\System.Management.dll",
				"runtimes\\win\\lib\\netstandard2.0\\System.Management.dll"
			};

			string path = null;
			Server.GetBestRuntimeDll(input, ref path);

			Assert.AreEqual(input[0], path);
		}
	}
}
