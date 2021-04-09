using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RemotingServer
{
    public interface ICallbackInterface
    {
        public void FireSomeAction(string nameOfAction);
    }
}