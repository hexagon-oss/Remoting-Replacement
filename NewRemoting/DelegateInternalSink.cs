using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
    /// <summary>
    /// This class acts as a server-side sink for delegates.
    /// An instance of this class is a registration to a single event
    /// </summary>
    internal class DelegateInternalSink
    {
        private readonly ClientSideInterceptor _interceptor;
        private readonly string _remoteObjectReference;
        private readonly MethodInfo _remoteMethodTarget;

        public DelegateInternalSink(ClientSideInterceptor interceptor, string remoteObjectReference, MethodInfo remoteMethodTarget)
        {
            _interceptor = interceptor;
            _remoteObjectReference = remoteObjectReference;
            _remoteMethodTarget = remoteMethodTarget;
        }

        public void ActionSink()
        {
            DoCallback();
        }

        public void ActionSink<T>(T arg1)
        {
            DoCallback(arg1);
        }

        public void ActionSink<T1, T2>(T1 arg1, T2 arg2)
        {
            DoCallback(arg1, arg2);
        }

        private void DoCallback(params object[] args)
        {
            ManualInvocation ri = new ManualInvocation(_remoteMethodTarget, args);
            ri.Proxy = this; // Works, because this instance is registered as proxy
            
            // TODO: Return result if the event is not of type void
            _interceptor.Intercept(ri);
        }

    }
}
