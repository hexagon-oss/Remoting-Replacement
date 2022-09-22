using System.Diagnostics;
using Moq;
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

		private Mock<IProcessWrapperFactory> _processWrapperFactoryMock;
		private Mock<IProcess> _processMock;

		private IRemoteConsole _remoteConsole;
		private Credentials _testCredentials;

		[SetUp]
		public void Setup()
		{
			_processWrapperFactoryMock = new Mock<IProcessWrapperFactory>(MockBehavior.Strict);
			_processMock = new Mock<IProcess>(MockBehavior.Strict);

			_testCredentials = Credentials.Create(TEST_USER, TEST_PASSWORD, DOMAIN, string.Empty);
			_remoteConsole = new RemoteConsole(TEST_HOST, _testCredentials, _processWrapperFactoryMock.Object);
		}

		[TearDown]
		public void TearDown()
		{
			_processWrapperFactoryMock.VerifyAll();
			_processMock.VerifyAll();
		}

		[Test]
		[TestCase("ping", false, "\\\\TestHost -u TestDomain\\TestUser -p TestPw -dfr ping")]
		[TestCase("ping", true, "\\\\TestHost -u TestDomain\\TestUser -p TestPw -dfr -i ping")]
		public void LaunchProcess_Command_Interactive(string cmdLine, bool interactive, string expectedArguments)
		{
			_processWrapperFactoryMock.Setup(a => a.CreateProcess()).Returns(_processMock.Object);
			_processMock.SetupSet(a => a.StartInfo = It.Is<ProcessStartInfo>(x => x.CreateNoWindow == true &&
				x.Arguments.Equals(expectedArguments) &&
				x.FileName.Equals(RemoteConsole.PAEXEC_EXECUTABLE) &&
				x.WindowStyle == ProcessWindowStyle.Hidden &&
				x.UseShellExecute == false &&
				x.RedirectStandardOutput == false &&
				x.RedirectStandardError == false &&
				x.RedirectStandardInput == false));
			_processMock.Setup(a => a.Start()).Returns(false);

			Assert.AreEqual(_processMock.Object,
				_remoteConsole.LaunchProcess(cmdLine, interactive));
		}

		[Test]
		[TestCase(@"%temp%\test.exe", false, false, false, false, null, null,
			@"\\TestHost -u TestDomain\TestUser -p TestPw -dfr %temp%\test.exe")]
		[TestCase(@"test.exe", true, true, false, false, null, null,
			@"\\TestHost -u TestDomain\TestUser -p TestPw -dfr -i test.exe")]
		[TestCase(@"test.exe", true, false, true, false, null, null,
			@"\\TestHost -u TestDomain\TestUser -p TestPw -dfr -i test.exe")]
		[TestCase(@"test.exe", true, false, false, true, null, null,
			@"\\TestHost -u TestDomain\TestUser -p TestPw -dfr -i test.exe")]
		[TestCase(@"test.exe", false, true, true, true, null, null,
			@"\\TestHost -u TestDomain\TestUser -p TestPw -dfr test.exe")]
		[TestCase(@"test.exe", false, true, true, true, "list.txt", "C:\\Temp",
			@"\\TestHost -u TestDomain\TestUser -p TestPw -dfr -w ""C:\Temp"" -f -clist list.txt -c test.exe")]
		public void CreateProcess(string cmdLine, bool interactive, bool redirectStandardOutput,
			bool redirectStandardError, bool redirectStandardInput, string filesListPath, string workingDirectory,
			string expectedArguments)
		{
			_processWrapperFactoryMock.Setup(a => a.CreateProcess()).Returns(_processMock.Object);
			_processMock.SetupSet(a => a.StartInfo = It.Is<ProcessStartInfo>(x => x.CreateNoWindow == true &&
				x.Arguments.Equals(expectedArguments) && x.FileName.Equals(RemoteConsole.PAEXEC_EXECUTABLE) &&
				x.WindowStyle == ProcessWindowStyle.Hidden &&
				x.UseShellExecute == false));
			Assert.AreEqual(_processMock.Object,
				_remoteConsole.CreateProcess(cmdLine, interactive, filesListPath, workingDirectory,
					redirectStandardOutput, redirectStandardError, redirectStandardInput));
		}

		[Test]
		[TestCase(@"%temp%\test.exe", true, null, "temp",
			@"\\TestHost -u TestDomain\TestUser -p TestPw -d -cnodel -dfr -w ""temp"" -csrc %temp%\test.exe -c test.exe")]
		[TestCase(@"%temp%\test.exe", false, null, "temp",
			@"\\TestHost -u TestDomain\TestUser -p TestPw -d -cnodel -dfr -w ""temp"" %temp%\test.exe")]
		public void CreateProcessConsole(string cmdLine, bool extraPath, string filesListPath, string workingDirectory,
			string expectedArguments)
		{
			_processWrapperFactoryMock.Setup(a => a.CreateProcess()).Returns(_processMock.Object);
			_processWrapperFactoryMock.Setup(a => a.CreateProcess()).Returns(_processMock.Object);
			_processMock.SetupSet(a => a.StartInfo = It.Is<ProcessStartInfo>(x => x.CreateNoWindow == true &&
				x.Arguments.Equals(expectedArguments) && x.FileName.Equals(RemoteConsole.PAEXEC_EXECUTABLE) &&
				x.WindowStyle == ProcessWindowStyle.Hidden &&
				x.UseShellExecute == false));
			Assert.AreEqual(_processMock.Object,
				_remoteConsole.CreateProcess(cmdLine, false, filesListPath, workingDirectory, false, false, false, true,
					extraPath));
		}
	}
}
