using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.Interfaces;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal class MessageHandlerTests
	{
		public MessageHandler Handler { get; }
		private DefaultProxyBuilder _builder;
		private ProxyGenerator _proxy;
		private InstanceManager _instanceManager;
		private FormatterFactory _formatterFactory;
		private ClientSideInterceptor _sideInterceptor;

		public MessageHandlerTests()
		{
			_builder = new DefaultProxyBuilder();
			_proxy = new ProxyGenerator(_builder);
			_instanceManager = new InstanceManager(_proxy, null);
			_formatterFactory = new FormatterFactory(_instanceManager);

			Handler = new MessageHandler(_instanceManager, _formatterFactory, null);
			// A client side has only one server, so there's also only one interceptor and only one server side
			_sideInterceptor = new ClientSideInterceptor(string.Empty, string.Empty, true, null, new MemoryStream(), Handler, null);
			Handler.AddInterceptor(_sideInterceptor);
		}

		[SetUp]
		public void SetUp()
		{
		}

		[Test]
		public void SerializeExceptionChain2()
		{
			Exception x = new RemotingException("Test1", new NotSupportedException("Inner1", new NotSupportedException("Inner2")));
			MemoryStream ms = new MemoryStream();
			BinaryWriter bw = new BinaryWriter(ms);
			BinaryReader br = new BinaryReader(ms);
			Handler.EncodeException(x, bw);
			long encodingLength = ms.Position;
			ms.Position = 0;
			var decoded = MessageHandler.DecodeException(br, "Dummy", Handler);
			long decodingLength = ms.Position;
			Assert.That(decoded, Is.Not.Null);
			Assert.That(decoded is RemotingException);
			Assert.That(decoded.InnerException, Is.Not.Null);
			Assert.That(decodingLength, Is.EqualTo(encodingLength));
		}

		[Test]
		public void SerializeExceptionChain1()
		{
			Exception x = new RemotingException("Test1", new NotSupportedException("Inner1"));
			MemoryStream ms = new MemoryStream();
			BinaryWriter bw = new BinaryWriter(ms);
			BinaryReader br = new BinaryReader(ms);
			Handler.EncodeException(x, bw);
			long encodingLength = ms.Position;
			ms.Position = 0;
			var decoded = MessageHandler.DecodeException(br, "Dummy", Handler);
			long decodingLength = ms.Position;
			Assert.That(decoded, Is.Not.Null);
			Assert.That(decoded is RemotingException);
			Assert.That(decoded.InnerException, Is.Not.Null);
			Assert.That(decodingLength, Is.EqualTo(encodingLength));
		}

		[Test]
		public void SerializeException()
		{
			Exception x = new RemotingException("Test1");
			MemoryStream ms = new MemoryStream();
			BinaryWriter bw = new BinaryWriter(ms);
			BinaryReader br = new BinaryReader(ms);
			Handler.EncodeException(x, bw);
			long encodingLength = ms.Position;
			ms.Position = 0;
			var decoded = MessageHandler.DecodeException(br, "Dummy", Handler);
			long decodingLength = ms.Position;
			Assert.That(decoded, Is.Not.Null);
			Assert.That(decoded is RemotingException);
			Assert.That(decoded.InnerException, Is.Null);
			Assert.That(decodingLength, Is.EqualTo(encodingLength));
		}

		[Test]
		public void SerializeExceptionWithProperties()
		{
			OperationCanceledException c = new OperationCanceledException();
			c.Source = "some source";

			var ldue = new LidarUnitException("Exception during sky lidar unit sensor communication",
				LidarUnitExceptionReason.SensorCommunicationException, c);

			MemoryStream ms = new MemoryStream();
			BinaryWriter bw = new BinaryWriter(ms);
			BinaryReader br = new BinaryReader(ms);

			Handler.EncodeException(ldue, bw);
			long encodingLength = ms.Position;
			ms.Position = 0;
			var decoded = MessageHandler.DecodeException(br, "Dummy", Handler) as LidarUnitException;
			long decodingLength = ms.Position;
			Assert.IsNotNull(decoded);
			Assert.True(decoded is LidarUnitException);
			Assert.NotNull(decoded.InnerException);
			Assert.AreEqual(encodingLength, decodingLength);
			Assert.AreEqual(LidarUnitExceptionReason.SensorCommunicationException, decoded.Reason);
			Assert.AreEqual(ldue.Message, decoded.Message);
			Assert.AreEqual(ldue.HResult, decoded.HResult);
			Assert.True(ldue.InnerException is OperationCanceledException);
			Assert.AreEqual(((ldue.InnerException as OperationCanceledException)!).Source, c.Source);
		}
	}
}
