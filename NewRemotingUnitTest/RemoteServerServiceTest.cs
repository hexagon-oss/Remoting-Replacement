using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Airborne.Generic.Remote.Loader;
using Moq;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal class RemoteServerServiceTest
	{
		private RemoteServerService m_service;
		private Mock<FileHashCalculator> m_hashCalculatorMock;

		[SetUp]
		public void SetUp()
		{
			m_hashCalculatorMock = new Mock<FileHashCalculator>();
			m_service = new RemoteServerService();
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
