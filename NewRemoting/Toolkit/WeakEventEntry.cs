using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// Stores the weak reference to the subscriber of the event and the method information
	/// </summary>
	internal class WeakEventEntry
	{
		private readonly Action<object, object[]> _invoker;
		private readonly MethodInfo _targetMethod;
		private readonly WeakReference _targetReference;

		/// <summary>
		/// Stores the reference in case of a transparent proxy or enclosure to keep the object alive
		/// </summary>
		private readonly object _normalReference;

		private bool _isInvalid;

		public WeakEventEntry(Action<object, object[]> invoker, MethodInfo targetMethod, object targetReference)
		{
			if (targetMethod.IsStatic) // if the method is static, create dummy target to simplify the code
			{
				_normalReference = targetReference = new object();
			}
			else
			{
				if (Client.IsRemoteProxy(targetReference) || targetReference is DelegateInternalSink)
				{
					// If the target is a transparent proxy, store the reference, otherwise it can be collected
					// Normally, this will even be our intermediate internal sink, because we reroute the delegate call through that.
					_normalReference = targetReference;
				}

				if (targetReference.GetType().GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Length != 0)
				{
					_normalReference = targetReference; // if enclosure, store the reference, otherwise it can be collected
				}
			}

			_targetReference = new WeakReference(targetReference);

			_invoker = invoker;
			_targetMethod = targetMethod;
			_isInvalid = false;
		}

		public bool IsInvalid
		{
			get
			{
				return _isInvalid || !_targetReference.IsAlive;
			}
		}

		public void Remove()
		{
			_isInvalid = true;
		}

		public bool Matches(object target, MethodInfo method)
		{
			bool res = false;
			object thisTarget = _targetReference.Target;
			if ((target != null && target == thisTarget) || method.IsStatic)
			{
				res = method == _targetMethod;
			}

			return res;
		}

		/// <summary>
		/// Checks that the target is still valid and invoke it
		/// Don't use IsInvalid here because it is too expensive and it is always valid if Target returns a value
		/// </summary>
		public bool Invoke(object[] arguments)
		{
			object target = _targetReference.Target;
			if (target != null && !_isInvalid)
			{
				_invoker(target, arguments);
				return true;
			}

			return false;
		}
	}
}
