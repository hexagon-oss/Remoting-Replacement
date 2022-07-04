using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NewRemoting
{
	internal class RemotingCallHeader
	{
		public const int HeaderSignature = 0x77889911;
		public const int TailerSignature = 0x58CCDDEE;
		private int _headerSignature;
		private RemotingFunctionType _function;
		private int _sequence;

		public RemotingCallHeader()
		{
		}

		public RemotingCallHeader(RemotingFunctionType command, int sequence)
		{
			_headerSignature = HeaderSignature;
			_function = command;
			_sequence = sequence;
		}

		public RemotingFunctionType Function => _function;

		public int Sequence => _sequence;

		public bool Validate()
		{
			return _headerSignature == HeaderSignature;
		}

		/// <summary>
		/// Write a header to the stream, obtaining a lock to write additional arguments
		/// </summary>
		/// <param name="w">The target binary writer</param>
		/// <returns>An object that must be disposed when finished writing the message</returns>
		public IDisposable WriteHeader(BinaryWriter w)
		{
			var lck = new StreamLock(w);
			w.Write(_headerSignature);
			w.Write((int)_function);
			w.Write(_sequence);
			return lck;
		}

		/// <summary>
		/// Write a message to a stream, not taking a lock (because the target object is a temporary stream only)
		/// </summary>
		/// <param name="w">The target binary writer</param>
		public void WriteHeaderNoLock(BinaryWriter w)
		{
			w.Write(_headerSignature);
			w.Write((int)_function);
			w.Write(_sequence);
		}

		public bool ReadFrom(BinaryReader r)
		{
			_headerSignature = r.ReadInt32();
			_function = (RemotingFunctionType)r.ReadInt32();
			_sequence = r.ReadInt32();
			return Validate();
		}

		private sealed class StreamLock : IDisposable
		{
			private readonly BinaryWriter _writerToLock;

			public StreamLock(BinaryWriter writerToLock)
			{
				_writerToLock = writerToLock;
				Monitor.Enter(_writerToLock.BaseStream);
			}

			public void Dispose()
			{
				Monitor.Exit(_writerToLock.BaseStream);
			}
		}
	}
}
