namespace NewRemoting.Toolkit
{
	internal interface IWeakEventInvoker
	{
		bool InvokeTarget(WeakEventEntry target, object[] arguments);
	}
}
