namespace NewRemoting.Toolkit
{
	public interface IWeakEvent<T>
		where T : class
	{
		int ClientCount
		{
			get;
		}

		/// <summary>
		/// This property is used like a method, to provide a type safe raiser. This is required because it is
		/// not possible to declare a Delegate as a constraint
		/// </summary>
		T Raise
		{
			get;
		}

		/// <summary>
		/// Adds a subscriber
		/// </summary>
		void Add(T invocationTarget);

		/// <summary>
		/// Removes a subscriber
		/// </summary>
		void Remove(T eh);

		void RemoveAll();
	}
}
