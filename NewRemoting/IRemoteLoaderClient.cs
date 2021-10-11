using System;
using System.Threading;

namespace NewRemoting
{
	/// <summary>
	/// Executes code on a remote machine
	/// </summary>
	public interface IRemoteLoaderClient : IRemoteLoader
	{
		/// <summary>
		/// Connects the remote loader to the remote system;
		/// </summary>
		void Connect(CancellationToken externalToken);

		/// <summary>
		/// Creates an object in a host process on a remote machine.
		/// Provides mocking ability.
		/// </summary>
		/// <typeparam name="TCreate">Concrete type of object to create</typeparam>
		/// <typeparam name="TReturn">Return type, may be an interface</typeparam>
		/// <param name="parameters">Constructor arguments</param>
		TReturn CreateObject<TCreate, TReturn>(object[] parameters)
			where TCreate : MarshalByRefObject, TReturn
			where TReturn : class;

		/// <summary>
		/// Creates an object in a host process on a remote machine.
		/// Provides mocking ability.
		/// </summary>
		/// <typeparam name="TCreate">Concrete type of object to create</typeparam>
		/// <typeparam name="TReturn">Return type, may be an interface</typeparam>
		TReturn CreateObject<TCreate, TReturn>()
			where TCreate : MarshalByRefObject, TReturn
			where TReturn : class;
	}
}
