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
			Assert.That(p.Length, Is.EqualTo(10));
			p.Write(buf, 0, 10);
			Assert.That(p.Length, Is.EqualTo(20));
		}

		[Test]
		public void BehaviorWhenSeeking()
		{
			byte[] buf = new byte[20];
			buf[0] = 10;
			buf[1] = 11;
			var p = new PooledMemoryStream(100);
			p.Write(buf, 0, 10);
			Assert.That(p.Length, Is.EqualTo(10));
			p.Position = 0;
			p.Write(buf, 0, 10);
			Assert.That(p.Length, Is.EqualTo(10));

			p.Position = 5;
			p.Write(buf, 0, 10);
			Assert.That(p.Length, Is.EqualTo(15));
		}

		[Test]
		public void BehaviorWhenWritingPastEndOfBuffer()
		{
			byte[] buf = new byte[200];
			buf[0] = 10;
			buf[1] = 11;
			var p = new PooledMemoryStream(100);
			p.Write(buf, 0, 50);
			Assert.That(p.Length, Is.EqualTo(50));
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
			Assert.That(p.Length, Is.EqualTo(50));
			p.SetLength(10);
			Assert.That(p.Length, Is.EqualTo(10));
			Assert.Throws<NotSupportedException>(() => p.SetLength(200));
		}
	}
}
