using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewRemoting.Toolkit;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal class PooledMemoryStreamTest
	{
		[Test]
		public void DefaultBehavior()
		{
			byte[] buf = new byte[20];
			buf[0] = 10;
			buf[1] = 11;
			var p = new PooledMemoryStream(100);
			p.Write(buf, 0, 10);
			Assert.AreEqual(10, p.Length);
			p.Write(buf, 0, 10);
			Assert.AreEqual(20, p.Length);
		}

		[Test]
		public void BehaviorWhenSeeking()
		{
			byte[] buf = new byte[20];
			buf[0] = 10;
			buf[1] = 11;
			var p = new PooledMemoryStream(100);
			p.Write(buf, 0, 10);
			Assert.AreEqual(10, p.Length);
			p.Position = 0;
			p.Write(buf, 0, 10);
			Assert.AreEqual(10, p.Length);

			p.Position = 5;
			p.Write(buf, 0, 10);
			Assert.AreEqual(15, p.Length);
		}

		[Test]
		public void BehaviorWhenWritingPastEndOfBuffer()
		{
			byte[] buf = new byte[200];
			buf[0] = 10;
			buf[1] = 11;
			var p = new PooledMemoryStream(100);
			p.Write(buf, 0, 50);
			Assert.AreEqual(50, p.Length);
			Assert.Throws<NotSupportedException>(() => p.Write(buf, 0, 100));
		}

		[Test]
		public void SetLength()
		{
			byte[] buf = new byte[200];
			buf[0] = 10;
			buf[1] = 11;
			var p = new PooledMemoryStream(100);
			p.Write(buf, 0, 50);
			Assert.AreEqual(50, p.Length);
			p.SetLength(10);
			Assert.AreEqual(10, p.Length);
			Assert.Throws<NotSupportedException>(() => p.SetLength(200));
		}
	}
}
