using System;
using Moq;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	public class DelegateInjectionTests
	{
		private TestClient _testee;
		private Mock<ITestInterface> _interfaceMock;

		[SetUp]
		public void SetUp()
		{
			_interfaceMock = new Mock<ITestInterface>();
			_testee = new TestClient(_interfaceMock.Object);
		}

		[Test]
		public void TestDelegateInjection()
		{
			_testee.Register();
			_interfaceMock.Raise(x => x.OnSomethingHappens += null, true);
			Assert.That(_testee.EventWasFired);
		}

		public interface ITestInterface
		{
			event Action<bool> OnSomethingHappens;
			void FireEvent();
		}

		public class TestServer : ITestInterface
		{
			public TestServer()
			{
			}

			public event Action<bool> OnSomethingHappens;

			public void FireEvent()
			{
				OnSomethingHappens?.Invoke(true);
			}
		}

		public class TestClient
		{
			private readonly ITestInterface _interfaceToMock;

			public TestClient(ITestInterface interfaceToMock)
			{
				_interfaceToMock = interfaceToMock;
				EventWasFired = false;
			}

			public bool EventWasFired { get; set; }

			private void SomethingHasHappened(bool obj)
			{
				EventWasFired = obj;
			}

			public void Register()
			{
				_interfaceToMock.OnSomethingHappens += SomethingHasHappened;
			}
		}
	}
}
