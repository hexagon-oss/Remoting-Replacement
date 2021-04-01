using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    public class MarshallableClass : MarshalByRefObject
    {
        private Random m_random;

        public MarshallableClass()
        {
            m_random = new Random();
        }

        public virtual int GetRandomNumber()
        {
            Console.WriteLine("Getting remote number");
            return m_random.Next();
        }
    }
}
