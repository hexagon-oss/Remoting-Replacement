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
		public void InstanceIsNotRemovedWhenAnotherGuyStillHasReference()
		{
			var myInstance = new MarshallableClass(100);
			string name1 = _instanceManager.GetIdForObject(myInstance, "1");
			string name2 = _instanceManager.GetIdForObject(myInstance, "2");
			Assert.AreEqual(name1, name2);

			_instanceManager.Remove(name1, "1");
			var myInstance2 = _instanceManager.GetObjectFromId(name2);
			Assert.True(ReferenceEquals(myInstance, myInstance2));
		}

		[Test]
		public void AddInstance()
		{
			var myInstance = new MarshallableClass(100);
			var myInstanceId = _instanceManager.InstanceIdentifier + "id";
			_instanceManager.AddInstance(myInstance, myInstanceId, "1", typeof(MarshallableClass));
#pragma warning disable CS0618
			var ii = _instanceManager.QueryInstanceInfo(myInstanceId);
#pragma warning restore CS0618
			Assert.AreEqual(1, ii.ReferenceBitVector);
			Assert.IsFalse(ii.IsReleased);
			_instanceManager.AddInstance(myInstance, myInstanceId, "2", typeof(MarshallableClass));
#pragma warning disable CS0618
			ii = _instanceManager.QueryInstanceInfo(myInstanceId);
#pragma warning restore CS0618
			Assert.AreEqual(3, ii.ReferenceBitVector);
			Assert.IsFalse(ii.IsReleased);
		}
	}
}
