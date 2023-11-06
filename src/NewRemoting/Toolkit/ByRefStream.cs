using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// A stream that references its input values instead of copying them.
	/// Can be used to chain data blocks together without allocating additional memory
	/// </summary>
	public class ByRefStream : Stream
	{
		private long _currentLength;
		private long _currentPosition;
		private List<Memory<byte>> _blocks;

		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => true;
		public override long Length => _currentLength;

		public override long Position
		{
			get
			{
				return _currentPosition;
			}
			set
			{
				_currentPosition = value;
			}
		}

		public ByRefStream()
		{
			_currentLength = 0;
			_blocks = new List<Memory<byte>>();
			_currentPosition = 0;
		}

		public override void Flush()
		{
			// Nothing to do
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int dataWritten = 0;
			while (dataWritten < count)
			{
				var (idx, blockOffset) = GetBlockIndex(Position);
				if (idx == -1) // End of data
				{
					return dataWritten;
				}

				int remainingInBlock = _blocks[idx].Length - blockOffset;
				if (remainingInBlock > count - dataWritten)
				{
					remainingInBlock = count - dataWritten;
				}

				_blocks[idx].Slice(blockOffset, remainingInBlock).CopyTo(new Memory<byte>(buffer, offset + dataWritten, remainingInBlock));
				dataWritten += remainingInBlock;
				Position += remainingInBlock;
			}

			return dataWritten;
		}

		private (int BlockNo, int OffsetInBlock) GetBlockIndex(long position)
		{
			int idx = 0;
			long offset = 0;
			if (position > _currentLength)
			{
				throw new InvalidOperationException("Cannot index entry past end of stream");
			}

			while (idx < _blocks.Count)
			{
				if (offset + _blocks[idx].Length > position)
				{
					return (idx, (int)(position - offset));
				}

				offset += _blocks[idx].Length;
				idx++;
			}

			return (-1, 0);
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			if (origin == SeekOrigin.Begin)
			{
				_currentPosition = offset;
			}
			else if (origin == SeekOrigin.Current)
			{
				_currentPosition = _currentPosition + offset;
				if (_currentPosition > _currentLength)
				{
					_currentPosition = _currentLength;
				}
			}
			else if (origin == SeekOrigin.End)
			{
				_currentPosition = Length - offset;
				if (_currentPosition < 0)
				{
					_currentPosition = 0;
				}
			}

			return _currentPosition;
		}

		public override void SetLength(long value)
		{
			throw new NotImplementedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			_blocks.Add(new Memory<byte>(buffer, offset, count));
			_currentPosition += count;
			_currentLength += count;
		}

		protected override void Dispose(bool disposing)
		{
			_blocks.Clear();
			base.Dispose(disposing);
		}
	}
}
