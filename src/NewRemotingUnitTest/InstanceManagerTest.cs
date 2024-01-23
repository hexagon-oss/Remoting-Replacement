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
			Assert.IsNotNull(_instanceManager.ProcessIdentifier);
			Assert.IsNotNull(_instanceManager.ProxyGenerator);
		}

		[Test]
		public void Create()
		{
			var myInstance = new MarshallableClass();
			string name1 = _instanceManager.RegisterRealObjectAndGetId(myInstance, "1");
			string name2 = _instanceManager.RegisterRealObjectAndGetId(myInstance, "1");
			Assert.AreEqual(name1, name2);

			var myInstance2 = _instanceManager.GetObjectFromId(name1, "type1", "method1", out bool wasDelegateTarget);
			Assert.True(ReferenceEquals(myInstance, myInstance2));
			Assert.False(wasDelegateTarget);
		}

		[Test]
		public void InstanceIsNotRemovedWhenAnotherGuyStillHasReference()
		{
			var myInstance = new MarshallableClass();
			string name1 = _instanceManager.RegisterRealObjectAndGetId(myInstance, "1");
			string name2 = _instanceManager.RegisterRealObjectAndGetId(myInstance, "2");
			Assert.AreEqual(name1, name2);

			_instanceManager.Remove(name1, "1", false);
			var myInstance2 = _instanceManager.GetObjectFromId(name2, "type1", "method1", out bool wasDelegateTarget);
			Assert.True(ReferenceEquals(myInstance, myInstance2));
			Assert.False(wasDelegateTarget);
		}

		[Test]
		public void AddInstanceFromDifferentClients()
		{
			var myInstance = new MarshallableClass();
			var myInstanceId = _instanceManager.ProcessIdentifier + "id";
			_instanceManager.AddInstance(myInstance, myInstanceId, "1", typeof(MarshallableClass), true);
#pragma warning disable CS0618
			var ii = _instanceManager.QueryInstanceInfo(myInstanceId);
#pragma warning restore CS0618
			Assert.AreEqual(1, ii.ReferenceBitVector);
			Assert.IsFalse(ii.IsReleased);
			_instanceManager.AddInstance(myInstance, myInstanceId, "2", typeof(MarshallableClass), true);
#pragma warning disable CS0618
			ii = _instanceManager.QueryInstanceInfo(myInstanceId);
#pragma warning restore CS0618
			Assert.AreEqual(3, ii.ReferenceBitVector);
			Assert.IsFalse(ii.IsReleased);
		}

		[Test]
		public void AddInstanceMultipleTimes()
		{
			var myInstance = new MarshallableClass("Instance1");
			_instanceManager.AddInstance(myInstance, "A", "1", myInstance.GetType(), false);
			var my2ndInstance = new MarshallableClass("Instance2");
			var addedInstance = _instanceManager.AddInstance(my2ndInstance, "A", "1", my2ndInstance.GetType(), false);
			Assert.IsTrue(ReferenceEquals(myInstance, addedInstance));
			Assert.IsFalse(ReferenceEquals(my2ndInstance, addedInstance));

			// If the last argument is true, the same operation throws
			Assert.Throws<InvalidOperationException>(() => _instanceManager.AddInstance(my2ndInstance, "A", "1", my2ndInstance.GetType(), true));
		}

		[Test]
		public void StrangeError()
		{
			var myInstance = new MarshallableClass("Instance1");
			Assert.Throws<ArgumentNullException>(() => _instanceManager.AddInstance(myInstance, null, "1", myInstance.GetType(), true));
		}
	}
}
