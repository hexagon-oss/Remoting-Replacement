using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
    public class MarshallableClass : MarshalByRefObject, IMarshallInterface
    {
        private ReferencedComponent _component;
        private ICallbackInterface _cb;
        public MarshallableClass()
        {
            _component = new ReferencedComponent();
            _cb = null;
            Identifier = Environment.TickCount64;
        }

        public virtual long Identifier
        {
            get;
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

        public virtual bool TryParseInt(string input, out int value)
        {
            return Int32.TryParse(input, out value);
        }

        public virtual void UpdateArgument(ref int value)
        {
            value += 2;
        }

        string IMarshallInterface.StringProcessId()
        {
            return GetCurrentProcessId().ToString();
        }

        public virtual void RegisterCallback(ICallbackInterface cb)
        {
            _cb = cb;
            DoCallback();
        }

        public virtual void DoCallback()
        {
            if (_cb != null)
            {
                _cb.FireSomeAction("Hello again!");
            }
        }

        public virtual ReferencedComponent GetComponent()
        {
            return _component;
        }

        public virtual T GetInterface<T>() where T : class
        {
            return _component as T;
        }
    }
}
