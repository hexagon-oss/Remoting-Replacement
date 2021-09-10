namespace NewRemoting
{
	internal interface IWeakEventInvoker
	{
		bool InvokeTarget(WeakEventEntry target, object[] arguments);
	}
}
