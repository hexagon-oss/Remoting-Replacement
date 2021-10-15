using System;

namespace NewRemoting
{
	/// <summary>
	///     Creates a objects in a host process on a remote machine
	/// </summary>
	public interface IRemoteLoader : IDisposable
	{
		/// <summary>
		///     Creates an object in a host process on a remote machine
		/// </summary>
		/// <typeparam name="T">Type of object to create</typeparam>
		/// <param name="parameters">Constructor arguments</param>
		T CreateObject<T>(object[] parameters)
			where T : MarshalByRefObject;

		/// <summary>
		///     Creates an object in a host process on a remote machine
		/// </summary>
		/// <typeparam name="T">Type of object to create</typeparam>
		T CreateObject<T>()
			where T : MarshalByRefObject;
	}
}
