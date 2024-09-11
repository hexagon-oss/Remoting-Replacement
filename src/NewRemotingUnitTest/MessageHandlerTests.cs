using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal class MessageHandlerTests
	{
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
			MessageHandler.EncodeException(x, bw);
			long encodingLength = ms.Position;
			ms.Position = 0;
			var decoded = MessageHandler.DecodeException(br, "Dummy");
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
			MessageHandler.EncodeException(x, bw);
			long encodingLength = ms.Position;
			ms.Position = 0;
			var decoded = MessageHandler.DecodeException(br, "Dummy");
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
			MessageHandler.EncodeException(x, bw);
			long encodingLength = ms.Position;
			ms.Position = 0;
			var decoded = MessageHandler.DecodeException(br, "Dummy");
			long decodingLength = ms.Position;
			Assert.That(decoded, Is.Not.Null);
			Assert.That(decoded is RemotingException);
			Assert.That(decoded.InnerException, Is.Null);
			Assert.That(decodingLength, Is.EqualTo(encodingLength));
		}
	}
}
