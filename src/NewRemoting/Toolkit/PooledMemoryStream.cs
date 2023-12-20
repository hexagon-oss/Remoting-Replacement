﻿using System;
using System.Buffers;
using System.IO;

namespace NewRemoting.Toolkit
{
	public class PooledMemoryStream : Stream, IManualSerialization
	{
		private byte[] _pooledObject;
		private int _contentLength;
		private MemoryStream _streamImplementation;

		public PooledMemoryStream(int size)
		{
			_pooledObject = ArrayPool<byte>.Shared.Rent(size);
			_streamImplementation = new MemoryStream(_pooledObject);
		}

		private PooledMemoryStream()
		{
			_pooledObject = null;
			_streamImplementation = null;
		}

		public override bool CanRead => _streamImplementation.CanRead;

		public override bool CanSeek => _streamImplementation.CanSeek;

		public override bool CanWrite => _streamImplementation.CanWrite;

		public override long Length => _contentLength;

		public override long Position
		{
			get => _streamImplementation.Position;
			set => _streamImplementation.Position = value;
		}

		public override void Flush()
		{
			_streamImplementation.Flush();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return _streamImplementation.Read(buffer, offset, count);
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			return _streamImplementation.Seek(offset, origin);
		}

		public override void SetLength(long value)
		{
			_streamImplementation.SetLength(value);
			_contentLength = (int)value;
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			_streamImplementation.Write(buffer, offset, count);
			_contentLength += count;
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (disposing && _pooledObject != null)
			{
				_streamImplementation?.Dispose();
				_streamImplementation = null;
				ArrayPool<byte>.Shared.Return(_pooledObject);
				_pooledObject = null;
			}
		}

		public void Serialize(BinaryWriter serializerTarget)
		{
			serializerTarget.Write(_contentLength);
			serializerTarget.Write(new ReadOnlySpan<byte>(_streamImplementation.GetBuffer(), 0, _contentLength));
		}

		public void Deserialize(BinaryReader serializerSource)
		{
			_contentLength = serializerSource.ReadInt32();
			_pooledObject = ArrayPool<byte>.Shared.Rent(_contentLength);
			_streamImplementation = new MemoryStream(_pooledObject, 0, _contentLength, true);
			_streamImplementation.Position = 0;
		}
	}
}