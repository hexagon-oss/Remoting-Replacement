using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NewRemoting;

namespace SampleServerClasses
{
	public class TransientServer : MarshalByRefObject, ITransientServer
	{
		private Client _client;
		private Process _serverProcess;
		private IRemoteServerService _remoteOperationsServer;

		public TransientServer()
		{
		}

		void ITransientServer.Init(int port)
		{
			_serverProcess = Process.Start("RemotingServer.exe", $"-p {port}");

			// Port is currently hardcoded
			_client = new Client("localhost", port, null, new ConnectionSettings(), new List<JsonConverter>());
			_remoteOperationsServer = _client.RequestRemoteInstance<IRemoteServerService>();
			if (_remoteOperationsServer == null)
			{
				throw new NotSupportedException("Couldn't query the remote infrastructure interface");
			}
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				_remoteOperationsServer = null;
				if (_client != null)
				{
					_client.ShutdownServer();
					_client.Dispose();
					_client = null;
				}

				if (_serverProcess != null)
				{
					_serverProcess.WaitForExit(2000);
					_serverProcess.Kill();
					_serverProcess = null;
				}
			}
		}

		public virtual void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		T ITransientServer.GetTransientInterface<T>()
		{
			return _client.RequestRemoteInstance<T>();
		}

		T ITransientServer.CreateTransientClass<T>()
		{
			return _client.CreateRemoteInstance<T>();
		}
	}
}
