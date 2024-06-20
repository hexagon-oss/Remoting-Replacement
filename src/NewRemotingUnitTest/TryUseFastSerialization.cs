using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NewRemoting;
using NUnit.Framework;
using SampleServerClasses;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal class TryUseFastSerialization
	{
		private MessageHandler _messageHandler;
		private Mock<Stream> _streamMock;
		private InstanceManager _instanceManager;

		[SetUp]
		public void SetUp()
		{
			_instanceManager = new InstanceManager(new ProxyGenerator(), null);
			_streamMock = new Mock<Stream>();
			_streamMock.Setup(x => x.CanRead).Returns(true);
			_messageHandler = new MessageHandler(_instanceManager, new FormatterFactory(_instanceManager), NullLogger.Instance);
			_messageHandler.AddInterceptor(new ClientSideInterceptor("OtherSide", "ThisSide", true, new ConnectionSettings(), _streamMock.Object, _messageHandler, null));
		}

		[Test]
		public void SerializeInt()
		{
			MemoryStream ms = new MemoryStream();
			var w = new BinaryWriter(ms, MessageHandler.DefaultStringEncoding);
			_messageHandler.TryUseFastSerialization(w, typeof(Int32), 10);
			ms.Position = 0;
			var r = new BinaryReader(ms, MessageHandler.DefaultStringEncoding);
			object result =
				_messageHandler.ReadArgumentFromStream(r, MethodBase.GetCurrentMethod(), null, true, null, "Blah", new ConnectionSettings());

			Assert.IsNotNull(result);
			Int32 casted = (Int32)result;
			Assert.AreEqual(10, casted);
		}

		[Test]
		public void SerializeString()
		{
			string test = "this is a test string";
			MemoryStream ms = new MemoryStream();
			var w = new BinaryWriter(ms, MessageHandler.DefaultStringEncoding);
			_messageHandler.TryUseFastSerialization(w, typeof(string), test);
			ms.Position = 0;
			var r = new BinaryReader(ms, MessageHandler.DefaultStringEncoding);
			object result =
				_messageHandler.ReadArgumentFromStream(r, MethodBase.GetCurrentMethod(), null, true, null, "Blah", new ConnectionSettings());

			Assert.IsNotNull(result);
			Assert.AreEqual(test, result);
		}

		[Test]
		public void SerializeClass()
		{
			var test = new SerializableClassWithMarshallableMembers(2, null);
			MemoryStream ms = new MemoryStream();
			var w = new BinaryWriter(ms, MessageHandler.DefaultStringEncoding);
			_messageHandler.WriteArgumentToStream(w, test, "Blah", new ConnectionSettings());
			_messageHandler.WriteArgumentToStream(w, (int)99, "Blah", new ConnectionSettings());
			byte[] data = ms.GetBuffer();
			string text = Encoding.UTF8.GetString(data.AsSpan(4));
			ms.Position = 0;
			var r = new BinaryReader(ms, MessageHandler.DefaultStringEncoding);
			SerializableClassWithMarshallableMembers result = (SerializableClassWithMarshallableMembers)_messageHandler.ReadArgumentFromStream(r, MethodBase.GetCurrentMethod(), null, true, null, "Blah", new ConnectionSettings());
			int x = (int)_messageHandler.ReadArgumentFromStream(r, MethodBase.GetCurrentMethod(), null, true, null, "Blah", new ConnectionSettings());
			Assert.IsNotNull(result);
			Assert.AreEqual(test.Idx, result.Idx);
			Assert.AreEqual(99, x); // to verify the stream is still in sync
		}
	}
}
