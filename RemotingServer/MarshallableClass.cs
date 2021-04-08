using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    public class MarshallableClass : MarshalByRefObject, IMarshallInterface
    {
        private ReferencedComponent _component;
        public MarshallableClass()
        {
            _component = new ReferencedComponent();
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

        public virtual int AddValues(int a, int b)
        {
            return a + b;
        }

        string IMarshallInterface.StringProcessId()
        {
            return GetCurrentProcessId().ToString();
        }

        public virtual ReferencedComponent GetComponent()
        {
            return _component;
        }
    }
}
