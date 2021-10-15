using System;
using System.Threading;

namespace NewRemoting
{
	/// <summary>
	/// Executes code on a remote machine
	/// </summary>
	public interface IRemoteLoaderClient : IDisposable
	{
		/// <summary>
		/// Get the internal <see cref="Client"/> instance, to perform advanced service queries.
		/// </summary>
		Client RemoteClient
		{
			get;
		}

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

		/// <summary>
		/// Retrieve a registered instance of the given type
		/// </summary>
		/// <typeparam name="T">Type to query</typeparam>
		/// <returns>A reference to an instance of the type from the remote service registry, or null if no
		/// such instance exists</returns>
		T RequestRemoteInstance<T>()
			where T : class;
	}
}
