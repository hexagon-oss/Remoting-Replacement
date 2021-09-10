using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace NewRemoting
{
	/// <summary>
	/// This class is required to share a invoker cache among all generic types of WeakEvent
	/// </summary>
	public abstract class WeakEventBase
	{
		private static readonly ConcurrentDictionary<MethodInfo, Action<object, object[]>> InvokerCache = new ConcurrentDictionary<MethodInfo, Action<object, object[]>>();

		/// <summary>
		/// Creates a method which takes the event target as first argument and a object array with the parameter as second arguments
		/// CreatedMethod: Foo(targetObject, arguments). This method calls the method passed with "method" argument on the "targetObject" with the passed arguments
		/// </summary>
		internal Action<object, object[]> CreateInvoker(MethodInfo method)
		{
			Action<object, object[]> invoker;
			if (InvokerCache.TryGetValue(method, out invoker))
			{
				return invoker;
			}

			ParameterInfo[] invokedMethodParameters = method.GetParameters();
			Type[] invokerParameterTypes = new[] { typeof(object), typeof(object[]) };

			DynamicMethod dm = new DynamicMethod("Invoker", null, invokerParameterTypes, method.DeclaringType);
			ILGenerator il = dm.GetILGenerator();

			if (!method.IsStatic)
			{
				il.Emit(OpCodes.Ldarg_0); // load the target object
			}

			for (int i = 0; i < invokedMethodParameters.Length; i++)
			{
				il.Emit(OpCodes.Ldarg_1); // parameter array to stack
				il.Emit(OpCodes.Ldc_I4_S, i); // parameter index
				il.Emit(OpCodes.Ldelem_Ref); // load object reference at array index
				il.Emit(OpCodes.Unbox_Any, invokedMethodParameters[i].ParameterType);
			}

			il.EmitCall(OpCodes.Call, method, null);
			il.Emit(OpCodes.Ret);

			invoker = (Action<object, object[]>)dm.CreateDelegate(typeof(Action<object, object[]>));
			InvokerCache.TryAdd(method, invoker);
			return invoker;
		}

		public static IWeakEvent<T> CreateRemoteAwareAsync<T>(Action<AggregateException> onException = null)
			where T : class
		{
			return Create<T>(onException, true, true);
		}

		public static IWeakEvent<T> CreateRemoteAware<T>(Action<AggregateException> onException = null,  bool async = false)
			where T : class
		{
			return Create<T>(onException, async, true);
		}

		public static IWeakEvent<T> Create<T>(Action<AggregateException> onException = null, bool async = false, bool remoteAware = false)
			where T : class
		{
			IWeakEventInvoker invoker = null;
			if (remoteAware)
			{
				invoker = new InvokerRemoteAware();
			}

			return async ? new WeakEventAsync<T>(onException, invoker) : new WeakEvent<T>(onException, invoker);
		}
	}
}
