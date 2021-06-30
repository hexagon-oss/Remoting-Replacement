using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
			_service = new RemoteServerService();
		}

		[Test]
		public void ClassIsNotSealed()
		{
			// To simplify the remote infrastructure at this point, it must be possible to create a proxy of this class
			var t = typeof(RemoteServerService);
			Assert.False(t.IsSealed);
		}
	}
}
