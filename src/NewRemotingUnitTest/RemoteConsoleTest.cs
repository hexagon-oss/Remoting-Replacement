using System.Diagnostics;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal sealed class RemoteConsoleTest
	{
		private const string TEST_USER = "TestUser";
		private const string TEST_PASSWORD = "TestPw";
		private const string TEST_HOST = "TestHost";
		private const string DOMAIN = "TestDomain";

		private IRemoteConsole _remoteConsole;
		private Credentials _testCredentials;

		[SetUp]
		public void Setup()
		{
			_testCredentials = Credentials.Create(TEST_USER, TEST_PASSWORD, DOMAIN, string.Empty);
			_remoteConsole = new RemoteConsole(TEST_HOST, _testCredentials);
		}

		[Test]
		[TestCase(@"%temp%\test.exe", false, false, false, false, null, null, ExpectedResult = @"\\TestHost -u TestDomain\TestUser -p TestPw -dfr %temp%\test.exe")]
		[TestCase(@"test.exe", true, true, false, false, null, null, ExpectedResult = @"\\TestHost -u TestDomain\TestUser -p TestPw -dfr -i test.exe")]
		[TestCase(@"test.exe", true, false, true, false, null, null, ExpectedResult = @"\\TestHost -u TestDomain\TestUser -p TestPw -dfr -i test.exe")]
		[TestCase(@"test.exe", true, false, false, true, null, null, ExpectedResult = @"\\TestHost -u TestDomain\TestUser -p TestPw -dfr -i test.exe")]
		[TestCase(@"test.exe", false, true, true, true, null, null, ExpectedResult = @"\\TestHost -u TestDomain\TestUser -p TestPw -dfr test.exe")]
		[TestCase(@"test.exe", false, true, true, true, "list.txt", "C:\\Temp", ExpectedResult = @"\\TestHost -u TestDomain\TestUser -p TestPw -dfr -w ""C:\Temp"" -f -clist list.txt -c test.exe")]
		public string CreateProcess(string cmdLine, bool interactive, bool redirectStandardOutput, bool redirectStandardError, bool redirectStandardInput, string filesListPath, string workingDirectory)
		{
			var process = _remoteConsole.CreateProcess(cmdLine, interactive, filesListPath, workingDirectory, redirectStandardOutput, redirectStandardError, redirectStandardInput);
			var startInfo = process.StartInfo;

			Assert.IsTrue(startInfo.CreateNoWindow);
			Assert.AreEqual(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
			Assert.AreEqual(RemoteConsole.PAEXEC_EXECUTABLE, startInfo.FileName);
			Assert.IsFalse(startInfo.UseShellExecute);
			Assert.AreEqual(redirectStandardOutput, startInfo.RedirectStandardOutput);
			Assert.AreEqual(redirectStandardError, startInfo.RedirectStandardError);
			Assert.AreEqual(redirectStandardInput, startInfo.RedirectStandardInput);
			return startInfo.Arguments;
		}

		[Test]
		[TestCase(@"%temp%\test.exe", true, null, "temp", ExpectedResult = @"\\TestHost -u TestDomain\TestUser -p TestPw -d -cnodel -dfr -w ""temp"" -csrc %temp%\test.exe -c test.exe")]
		[TestCase(@"%temp%\test.exe", false, null, "temp", ExpectedResult = @"\\TestHost -u TestDomain\TestUser -p TestPw -d -cnodel -dfr -w ""temp"" %temp%\test.exe")]
		public string CreateProcessConsole(string cmdLine, bool extraPath, string filesListPath, string workingDirectory)
		{
			var process = _remoteConsole.CreateProcess(cmdLine, false, filesListPath, workingDirectory, false, false, false, true, extraPath);
			var startInfo = process.StartInfo;

			Assert.IsTrue(startInfo.CreateNoWindow);
			Assert.AreEqual(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
			Assert.AreEqual(RemoteConsole.PAEXEC_EXECUTABLE, startInfo.FileName);
			Assert.IsFalse(startInfo.UseShellExecute);
			return startInfo.Arguments;
		}
	}
}
