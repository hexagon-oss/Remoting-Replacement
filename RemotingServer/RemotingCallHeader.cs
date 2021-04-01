using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RemotingCallHeader
    {
        public const int HeaderSignature = 0x77889911;
        private int _headerSignature;
        private RemotingFunctionType _function;
        private int _sequence;

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

        public void WriteTo(BinaryWriter w)
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
    }
}
