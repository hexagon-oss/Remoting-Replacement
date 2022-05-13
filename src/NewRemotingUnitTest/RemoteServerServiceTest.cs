using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal class RemoteServerServiceTest
	{
		private RemoteServerService _service;
		private Mock<FileHashCalculator> _hashCalculatorMock;

		[SetUp]
		public void SetUp()
		{
			_hashCalculatorMock = new Mock<FileHashCalculator>();
			_service = new RemoteServerService(new Server(0, null), _hashCalculatorMock.Object, NullLogger.Instance);
		}

		[Test]
		public void ClassIsNotSealed()
		{
			// To simplify the remote infrastructure at this point, it must be possible to create a proxy of this class
			var t = typeof(RemoteServerService);
			Assert.False(t.IsSealed);
		}

		[Test]
		public void CalculateHash()
		{
			_service.PrepareFileUpload("Test.dat", new byte[] { 0, 1, 2, 3 });
		}
	}
}
