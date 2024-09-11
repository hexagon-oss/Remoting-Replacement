using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	[TestFixture]
	internal class PerformanceTest
	{
		private string _data;

		[SetUp]
		public void SetUp()
		{
			_data = "This is a test string with some characters: blahblahblah - Moreblahblah." +
					"What for is this? Testing... öäü$Ä.";
		}

		// Despite the more complex encoding, this is faster than unicode
		[Test]
		public void TestStringSerializationUtf8()
		{
			TestStringSerialization(Encoding.UTF8);
		}

		[Test]
		public void TestStringSerializationUnicode()
		{
			TestStringSerialization(Encoding.Unicode);
		}

		/// <summary>
		/// I expected this to be faster, but it is a lot slower than even the unicode encoding, just because the span ctor calls IsValueType, which is very expensive.
		/// </summary>
		[Test]
		public void TestStringSerializationFast()
		{
			TestStringSerialization(new DirectBinaryEncoding());
		}

		public void TestStringSerialization(Encoding encoding)
		{
			MemoryStream ms = new MemoryStream();
			BinaryWriter w = new BinaryWriter(ms, encoding, true);
			for (int i = 0; i < 100000; i++)
			{
				w.Write(_data);
			}

			w.Close();
			ms.Position = 0;
			BinaryReader r = new BinaryReader(ms, encoding, true);
			var read = r.ReadString();
			Assert.That(read, Is.EqualTo(_data));
		}

		private sealed class DirectBinaryEncoding : Encoding
		{
			public override int GetByteCount(char[] chars, int index, int count)
			{
				return count * 2;
			}

			public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
			{
				int bytesFree = bytes.Length - byteIndex;
				if (bytesFree < charCount * 2)
				{
					throw new ArgumentException("Not enough room in target array");
				}

				Span<byte> input = MemoryMarshal.Cast<char, byte>(chars.AsSpan(charIndex, charCount));
				Span<byte> output = bytes.AsSpan(byteIndex);
				input.CopyTo(output);
				return input.Length;
			}

			public override int GetCharCount(byte[] bytes, int index, int count)
			{
				return count / 2;
			}

			public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
			{
				int charsFree = chars.Length - charIndex;
				if (charsFree < byteCount / 2)
				{
					throw new ArgumentException("Not enough room in target array");
				}

				Span<char> input = MemoryMarshal.Cast<byte, char>(bytes.AsSpan(byteIndex, byteCount));
				Span<char> output = chars.AsSpan(charIndex);
				input.CopyTo(output);
				return input.Length;
			}

			public override int GetMaxByteCount(int charCount)
			{
				return charCount * 2;
			}

			public override int GetMaxCharCount(int byteCount)
			{
				return byteCount / 2;
			}
		}
	}
}
