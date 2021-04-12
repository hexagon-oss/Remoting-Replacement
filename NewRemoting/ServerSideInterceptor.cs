using System;
using Castle.DynamicProxy;

namespace NewRemoting
{
    public class ServerSideInterceptor : IInterceptor
    {
        private readonly RemotingServer _remotingServer;

        public ServerSideInterceptor(RemotingServer remotingServer)
        {
            _remotingServer = remotingServer;
        }

        public void Intercept(IInvocation invocation)
        {
            throw new NotImplementedException();
        }
    }
}