using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    public class ReferencedComponent : MarshalByRefObject
    {
        private int _data;

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

        public virtual int SuperNumber()
        {
            return _data;
        }
    }
}
