using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    public interface IMyComponentInterface
    {
        event Action<DateTime> TimeChanged;

        DateTime QueryTime();

        Stream GetRemoteStream(string fileName);

        void StartTiming();

        string ProcessName();
    }
}
