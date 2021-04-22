using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
    public class ConstructorArgument : MarshalByRefObject
    {
        public ConstructorArgument()
        {
            // Used by remoting infrastructure
        }

        public ConstructorArgument(IMyComponentInterface reverseInterface)
        {
            ReverseInterface = reverseInterface;
        }

        public virtual IMyComponentInterface ReverseInterface { get; }
    }
}
