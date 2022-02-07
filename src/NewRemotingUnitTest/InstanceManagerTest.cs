using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;
using Moq;
using NewRemoting;
using NUnit.Framework;
using SampleServerClasses;

namespace NewRemotingUnitTest
{
	[TestFixture]
	[NonParallelizable]
	internal class InstanceManagerTest
	{
		private InstanceManager _instanceManager;

		[SetUp]
		public void SetUp()
		{
			_instanceManager = new InstanceManager(new ProxyGenerator(), Mock.Of<ILogger>());
			_instanceManager.Clear(true);
		}

		[TearDown]
		public void TearDown()
		{
			_instanceManager.Clear(true);
			_instanceManager.Dispose();
		}

		[Test]
		public void Initialization()
		{
			Assert.IsNotNull(_instanceManager.InstanceIdentifier);
			Assert.IsNotNull(_instanceManager.ProxyGenerator);
		}

		[Test]
		public void Create()
		{
			var myInstance = new MarshallableClass(100);
			string name1 = _instanceManager.GetIdForObject(myInstance, "1");
			string name2 = _instanceManager.GetIdForObject(myInstance, "1");
			Assert.AreEqual(name1, name2);

			var myInstance2 = _instanceManager.GetObjectFromId(name1);
			Assert.True(ReferenceEquals(myInstance, myInstance2));
		}

		[Test]
		public void InstanceIsRemovedUponRequest()
		{
			var myInstance = new MarshallableClass(100);
			string name1 = _instanceManager.GetIdForObject(myInstance, "1");

			_instanceManager.Remove(name1, "1");

			Assert.Throws<InvalidOperationException>(() => _instanceManager.GetObjectFromId(name1));
		}

		[Test]
		public void InstanceIsNotRemovedWhenAnotherGuyStillHasReference()
		{
			var myInstance = new MarshallableClass(100);
			string name1 = _instanceManager.GetIdForObject(myInstance, "1");
			string name2 = _instanceManager.GetIdForObject(myInstance, "2");
			Assert.AreEqual(name1, name2);

			_instanceManager.Remove(name1, "1");
			var myInstance2 = _instanceManager.GetObjectFromId(name2);
			Assert.True(ReferenceEquals(myInstance, myInstance2));

			_instanceManager.Remove(name2, "2");
			Assert.Throws<InvalidOperationException>(() => _instanceManager.GetObjectFromId(name1));
		}
	}
}
