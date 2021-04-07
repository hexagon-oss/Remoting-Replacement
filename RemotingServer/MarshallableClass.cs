using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    public class MarshallableClass : MarshalByRefObject
    {
        public MarshallableClass()
        {
        }

        public virtual int GetSomeData()
        {
            Console.WriteLine("Getting remote number");
            return 4711;
        }

        public virtual int GetCurrentProcessId()
        {
            Process ps = Process.GetCurrentProcess();
            return ps.Id;
        }
    }
}
