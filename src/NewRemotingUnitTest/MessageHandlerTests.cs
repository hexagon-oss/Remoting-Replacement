using System;
using System.IO;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NewRemoting;
using NUnit.Framework;
using SampleServerClasses;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal class MessageHandlerTests
	{
		private MessageHandler Handler => _handler;
		private DefaultProxyBuilder _builder;
		private ProxyGenerator _proxy;
		private InstanceManager _instanceManager;
		private FormatterFactory _formatterFactory;
		private ClientSideInterceptor _sideInterceptor;
		private MessageHandler _handler;

		[SetUp]
		public void SetUp()
		{
			_builder = new DefaultProxyBuilder();
			_proxy = new ProxyGenerator(_builder);
			_instanceManager = new InstanceManager(_proxy, null);
			_formatterFactory = new FormatterFactory(_instanceManager);

			_handler = new MessageHandler(_instanceManager, _formatterFactory, null);
			// A client side has only one server, so there's also only one interceptor and only one server side
			_sideInterceptor = new ClientSideInterceptor(string.Empty, string.Empty, true, null, new MemoryStream(), Handler, Mock.Of<ILogger>());
			Handler.AddInterceptor(_sideInterceptor);
			_sideInterceptor.Start();
		}

		[TearDown]
		public void TearDown()
		{
			_sideInterceptor.Dispose();
			_sideInterceptor = null;
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

			var ldue = new ExceptionWithCustomerProperty("Exception message",
				SomeExceptionReason.SensorCommunicationException, c);

			MemoryStream ms = new MemoryStream();
			BinaryWriter bw = new BinaryWriter(ms);
			BinaryReader br = new BinaryReader(ms);

			Handler.EncodeException(ldue, bw);
			long encodingLength = ms.Position;
			ms.Position = 0;
			ExceptionWithCustomerProperty decoded = MessageHandler.DecodeException(br, "Dummy", Handler) as ExceptionWithCustomerProperty;
			long decodingLength = ms.Position;
			Assert.That(decoded, Is.Not.Null);
			Assert.That(decoded.InnerException, Is.Not.Null);
			Assert.That(encodingLength, Is.EqualTo(decodingLength));

			Assert.That(decoded.Reason, Is.EqualTo(SomeExceptionReason.SensorCommunicationException));
			Assert.That(ldue.Message, Is.EqualTo(decoded.Message));
			Assert.That(ldue.HResult, Is.EqualTo(decoded.HResult));
			Assert.That(ldue.InnerException is OperationCanceledException, Is.True);
			Assert.That(((ldue.InnerException as OperationCanceledException)!).Source, Is.EqualTo(c.Source));
		}

		[Test]
		public void SerializeCustomExceptionWithProperties()
		{
			var c = new CustomTestException("Something went wrong", "It really did", 2);

			MemoryStream ms = new MemoryStream();
			BinaryWriter bw = new BinaryWriter(ms);
			BinaryReader br = new BinaryReader(ms);

			Handler.EncodeException(c, bw);
			long encodingLength = ms.Position;
			ms.Position = 0;
			CustomTestException decoded = MessageHandler.DecodeException(br, "Dummy", Handler) as CustomTestException;
			long decodingLength = ms.Position;
			Assert.That(decoded, Is.Not.Null);
			Assert.That(decoded.InnerException, Is.Null);
			Assert.That(encodingLength, Is.EqualTo(decodingLength));

			Assert.That(decoded.CustomMessagePart, Is.EqualTo("It really did"));
			Assert.That(decoded.Message, Is.EqualTo(c.Message));
			Assert.That(decoded.NumberOfErrors, Is.EqualTo(2));
		}
	}
}
