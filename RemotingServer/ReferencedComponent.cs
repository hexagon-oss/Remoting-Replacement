using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    public class ReferencedComponent : MarshalByRefObject, IMyComponentInterface, IDisposable
    {
        private int _data;

        public event Action<DateTime> TimeChanged;
        
        public ReferencedComponent()
        {
            _data = GetHashCode();
        }

        public virtual int Data
        {
            get
            {
                return _data;
            }

            set
            {
                _data = value;
            }
        }

        public string ProcessName()
        {
            return Process.GetCurrentProcess().ProcessName;
        }

        public virtual int SuperNumber()
        {
            return _data;
        }

        public DateTime QueryTime()
        {
            TimeChanged?.Invoke(DateTime.Now);
            return DateTime.Now;
        }

        public Stream GetRemoteStream(string fileName)
        {
            return new FileStream(fileName, FileMode.Open);
        }

        public void Dispose()
        {
        }
    }
}
