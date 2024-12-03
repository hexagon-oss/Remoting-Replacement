using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	public sealed class PooledMemoryStream : Stream, IManualSerialization
	{
		private readonly object _lock = new object();
		private byte[] _pooledObject;
		private int _contentLength;
		private MemoryStream _streamImplementation;

		public PooledMemoryStream(int size)
		{
			// This currently only supports streams up to 2GB in size
			if (size > Int32.MaxValue)
			{
				throw new ArgumentOutOfRangeException(nameof(size), "Size must be less than 2GB");
			}

			_pooledObject = ArrayPool<byte>.Shared.Rent(size);
			_streamImplementation = new MemoryStream(_pooledObject);
		}

		private PooledMemoryStream()
		{
			_pooledObject = null;
			_streamImplementation = null;
		}

		public override bool CanRead
		{
			get
			{
				lock (_lock)
				{
					if (_streamImplementation == null)
					{
						return false;
					}

					return _streamImplementation.CanRead;
				}
			}
		}

		public override bool CanSeek
		{
			get
			{
				lock (_lock)
				{
					if (_streamImplementation == null)
					{
						return false;
					}

					return _streamImplementation.CanSeek;
				}
			}
		}

		public override bool CanWrite
		{
			get
			{
				lock (_lock)
				{
					if (_streamImplementation == null)
					{
						return false;
					}

					return _streamImplementation.CanWrite;
				}
			}
		}

		public override long Length
		{
			get { return _contentLength; }
		}

		public override long Position
		{
			get
			{
				lock (_lock)
				{
					if (_streamImplementation == null)
					{
						return 0;
					}

					return _streamImplementation.Position;
				}
			}
			set
			{
				lock (_lock)
				{
					if (_streamImplementation != null)
					{
						_streamImplementation.Position = value;
					}
				}
			}
		}

		public override void Flush()
		{
			lock (_lock)
			{
				if (_streamImplementation == null)
				{
					return;
				}

				_streamImplementation.Flush();
			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			lock (_lock)
			{
				if (_streamImplementation == null)
				{
					return 0;
				}

				if (_streamImplementation.Position >= _contentLength)
				{
					return 0;
				}

				return _streamImplementation.Read(buffer, offset, count);
			}
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<int>(cancellationToken);
			}

			var impl = _streamImplementation;
			if (impl == null)
			{
				return Task.FromResult(0);
			}

			if (impl.Position >= _contentLength)
			{
				return Task.FromResult(0);
			}

			return base.ReadAsync(buffer, offset, count, cancellationToken);
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			lock (_lock)
			{
				if (_streamImplementation == null)
				{
					return 0;
				}

				return _streamImplementation.Seek(offset, origin);
			}
		}

		public override void SetLength(long value)
		{
			_streamImplementation.SetLength(value);
			_contentLength = (int)value;
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			_streamImplementation.Write(buffer, offset, count);
			// The content length expands only if the write happened at the end of the content
			_contentLength = (int)Math.Max(_contentLength, _streamImplementation.Position);
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (disposing && _pooledObject != null)
			{
				lock (_lock)
				{
					_streamImplementation?.Dispose();
					_streamImplementation = null;
				}

				ArrayPool<byte>.Shared.Return(_pooledObject);
				_pooledObject = null;
			}
		}

		public void Serialize(BinaryWriter serializerTarget)
		{
			serializerTarget.Write(_contentLength);
			serializerTarget.Write(_pooledObject, 0, _contentLength);
		}

		public void Deserialize(BinaryReader serializerSource)
		{
			_contentLength = serializerSource.ReadInt32();
			_pooledObject = ArrayPool<byte>.Shared.Rent(_contentLength);
			int alreadyRead = 0;
			while (alreadyRead < _contentLength)
			{
				// On slow(er) connections, sometimes a loop is required
				int len = serializerSource.Read(_pooledObject, alreadyRead, _contentLength - alreadyRead);
				if (len == 0)
				{
					throw new RemotingException("End of stream detected while deserializing object");
				}

				alreadyRead += len;
			}

			lock (_lock)
			{
				_streamImplementation = new MemoryStream(_pooledObject, 0, _contentLength, true);
				_streamImplementation.Position = 0;
			}
		}
	}
}
