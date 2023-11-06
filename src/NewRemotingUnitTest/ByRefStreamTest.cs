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
	public class ByRefStreamTest
	{
		[Test]
		public void GetSingleBlock()
		{
			using ByRefStream bs = new ByRefStream();
			byte[] b1 = new byte[] { 2, 3, 4, 5 };
			bs.Write(b1);
			bs.Write(b1, 2, 2);

			bs.Position = 0;
			byte[] b2 = new byte[4];
			Assert.AreEqual(4, bs.Read(b2, 0, 4));

			CollectionAssert.AreEqual(b1, b2);
		}

		[Test]
		public void GetMultipleBlocks()
		{
			using ByRefStream bs = new ByRefStream();
			byte[] b1 = new byte[] { 2, 3, 4, 5 };
			bs.Write(b1, 0, 2);
			bs.Write(b1, 2, 2);

			bs.Position = 0;
			byte[] b2 = new byte[4];
			Assert.AreEqual(4, bs.Read(b2, 0, 4));

			CollectionAssert.AreEqual(b1, b2);
		}

		[Test]
		public void GetAsMultipleBlocks()
		{
			using ByRefStream bs = new ByRefStream();
			byte[] b1 = new byte[] { 2, 3, 4, 5, 6, 7 };
			bs.Write(b1);

			bs.Position = 0;
			byte[] b2 = new byte[4];
			Assert.AreEqual(2, bs.Read(b2, 2, 2));

			Assert.AreEqual(2, b2[2]);
			Assert.AreEqual(3, b2[3]);
		}

		[Test]
		public void GetMultipleBlocksAsSingle()
		{
			using ByRefStream bs = new ByRefStream();
			byte[] b1 = new byte[] { 2, 3 };
			bs.Write(b1);
			bs.Write(b1);
			bs.Write(b1);

			bs.Position = 0;
			byte[] b2 = new byte[6];
			Assert.AreEqual(6, bs.Read(b2, 0, 6));

			Assert.AreEqual(2, b2[0]);
			Assert.AreEqual(3, b2[1]);
			Assert.AreEqual(2, b2[2]);
			Assert.AreEqual(3, b2[3]);
			Assert.AreEqual(2, b2[4]);
			Assert.AreEqual(3, b2[5]);
		}
	}
}
