﻿using System;
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
	public class ClientTest
	{
		private InstanceManager _instanceManager;

		[SetUp]
		public void SetUp()
		{
			var builder = new DefaultProxyBuilder();
			var proxy = new ProxyGenerator(builder);
			_instanceManager = new InstanceManager(proxy, Mock.Of<ILogger>());
		}

		[TearDown]
		public void TearDown()
		{
			_instanceManager.Dispose();
		}

		[Test]
		public void CanIdentifyProxy()
		{
			var proxy = _instanceManager.ProxyGenerator.CreateInterfaceProxyWithoutTarget<IMarshallInterface>();
			Assert.IsNotNull(proxy);
			Assert.True(Client.IsProxyType(proxy.GetType()));
			Assert.True(Client.IsRemoteProxy(proxy));

			IMarshallInterface mi = new MarshallableClass(nameof(CanIdentifyProxy));
			Assert.False(Client.IsProxyType(mi.GetType()));
			Assert.False(Client.IsRemoteProxy(mi));

			Assert.True(Client.IsRemotingCapable(mi));
		}

		[Test]
		public void CanQueryUnproxiedType()
		{
			var proxy = _instanceManager.ProxyGenerator.CreateInterfaceProxyWithoutTarget<IMarshallInterface>();
			Assert.That(proxy.GetType().Name.Contains("Proxy"));

			var notAproxy = Client.GetUnproxiedType(proxy);
			Assert.That(notAproxy, Is.EqualTo(typeof(IMarshallInterface)));
		}

		[Test]
		public void CanQueryUnproxiedTypeOfGenericClass()
		{
			var proxy = _instanceManager.ProxyGenerator.CreateClassProxy(typeof(List<int>));
			Assert.That(proxy.GetType().Name.Contains("Proxy"));

			var notAproxy = Client.GetUnproxiedType(proxy);
			Assert.That(notAproxy, Is.EqualTo(typeof(List<int>)));
		}

		[Test]
		public void CanQueryUnproxiedTypeManually1()
		{
			var proxy = _instanceManager.ProxyGenerator.CreateInterfaceProxyWithoutTarget<IMarshallInterface>();
			Assert.That(proxy.GetType().Name.Contains("Proxy"));

			var notAproxy = Client.ManualGetUnproxiedType(proxy.GetType());
			Assert.That(notAproxy, Is.EqualTo(typeof(IMarshallInterface)));
		}

		[Test]
		public void CanQueryUnproxiedTypeManually2()
		{
			var proxy = _instanceManager.ProxyGenerator.CreateClassProxy(typeof(WithManyInterfaces), new Type[] { typeof(IDisposable), typeof(IMarshallInterface) }, ProxyGenerationOptions.Default);
			Assert.That(proxy.GetType().Name.Contains("Proxy"));

			var notAproxy = Client.ManualGetUnproxiedType(proxy.GetType());
			Assert.That(notAproxy, Is.EqualTo(typeof(WithManyInterfaces)));
		}

		[Test]
		public void CanQueryUnproxiedTypeOfGenericClassManually()
		{
			var proxy = _instanceManager.ProxyGenerator.CreateClassProxy(typeof(List<int>));
			Assert.That(proxy.GetType().Name.Contains("Proxy"));

			var notAproxy = Client.ManualGetUnproxiedType(proxy.GetType());
			Assert.That(notAproxy, Is.EqualTo(typeof(List<int>)));
		}
	}
}
