using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	internal sealed class JsonSplitStream : Stream
	{
		private long _position;

		public JsonSplitStream(Stream baseStream, long length)
		{
			BaseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
			ArgumentOutOfRangeException.ThrowIfNegative(length, nameof(length));
			Length = length;
			_position = 0;
		}

		public Stream BaseStream
		{
			get;
		}

		public override long Length
		{
			get;
		}

		public override bool CanWrite => false;
		public override bool CanSeek => false;
		public override bool CanRead => true;

		public override long Position
		{
			get
			{
				return _position;
			}
			set
			{
				throw new InvalidOperationException("Cannot set position");
			}
		}

		public override void Flush()
		{
			BaseStream.Flush();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (Length - _position < count)
			{
				count = (int)(Length - _position);
			}

			if (count == 0)
			{
				return 0;
			}

			int ret = BaseStream.Read(buffer, offset, count);
			_position += ret;
			return ret;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException("Seeking is not supported");
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException("SetLength is not supported");
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException("Writing is not supported");
		}
	}
}
