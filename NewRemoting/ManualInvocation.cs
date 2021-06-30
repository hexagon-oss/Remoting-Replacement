using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;

namespace NewRemoting
{
	/// <summary>
	/// This stub is used for explicit calls to the Interceptor, to simulate a method call (i.e. to forward a delegate invocation)
	/// </summary>
	internal class ManualInvocation : IInvocation
	{
		public ManualInvocation(MethodBase method, object[] args)
		{
			if (method is MethodInfo mi)
			{
				Method = mi;
			}
			else if (method is ConstructorInfo ci)
			{
				Constructor = ci;
			}
			else
			{
				throw new ArgumentException("Invalid invocation type", nameof(method));
			}

			Arguments = args;
		}

		public object GetArgumentValue(int index)
		{
			throw new NotImplementedException();
		}

		public MethodInfo GetConcreteMethod()
		{
			throw new NotImplementedException();
		}

		public MethodInfo GetConcreteMethodInvocationTarget()
		{
			throw new NotImplementedException();
		}

		public void Proceed()
		{
			throw new NotImplementedException();
		}

		public IInvocationProceedInfo CaptureProceedInfo()
		{
			throw new NotImplementedException();
		}

		public void SetArgumentValue(int index, object value)
		{
			throw new NotImplementedException();
		}

		public object[] Arguments { get; }
		public Type[] GenericArguments { get; }
		public object InvocationTarget { get; }
		public MethodInfo Method { get; }
		public MethodInfo MethodInvocationTarget { get; }

		public ConstructorInfo Constructor { get; }

		public object Proxy { get; set; }
		public object ReturnValue { get; set; }
		public Type TargetType { get; }
	}
}
