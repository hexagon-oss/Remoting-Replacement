using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal class TryUseFastSerialization
	{
		private MessageHandler _messageHandler;
		private Mock<Stream> _streamMock;

		[SetUp]
		public void SetUp()
		{
			_streamMock = new Mock<Stream>();
			_streamMock.Setup(x => x.CanRead).Returns(true);
			_messageHandler = new MessageHandler(null, null);
			_messageHandler.AddInterceptor(new ClientSideInterceptor("OtherSide", "ThisSide", true, _streamMock.Object, _messageHandler, null));
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
				_messageHandler.ReadArgumentFromStream(r, MethodBase.GetCurrentMethod(), null, true, null, "Blah");

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
				_messageHandler.ReadArgumentFromStream(r, MethodBase.GetCurrentMethod(), null, true, null, "Blah");

			Assert.IsNotNull(result);
			Assert.AreEqual(test, result);
		}
	}
}
