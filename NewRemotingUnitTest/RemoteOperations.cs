using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using NewRemoting;
using NUnit.Framework;
using SampleServerClasses;

namespace NewRemotingUnitTest
{
    [TestFixture]
    public class RemoteOperations
    {
        private Process _serverProcess;
        private Client _client;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _serverProcess = Process.Start("RemotingServer.exe");
            Assert.IsNotNull(_serverProcess);

            // Port is currently hardcoded
            _client = new Client("localhost", Client.DefaultNetworkPort);
            _client.Start();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_client != null)
            {
                _client.ShutdownServer();
                _client.Dispose();
                _client = null;
            }

            if (_serverProcess != null)
            {
                Assert.That(_serverProcess.WaitForExit(2000));
                _serverProcess.Kill();
                _serverProcess = null;
            }
        }

        [Test]
        public void LocalObjectIsNotProxy()
        {
            var instance = new MarshallableClass();
            // There have been reports that this returns false positives
            Assert.False(Client.IsRemoteProxy(instance));
        }

        [Test]
        public void GetInitialRemoteInstance()
        {
            var instance = GetRemoteInstance();
            Assert.IsNotNull(instance);
            Assert.That(Client.IsRemoteProxy(instance));
        }

        [Test]
        public void RemoteInstanceCanBeCalled()
        {
            var instance = GetRemoteInstance();
            int remotePid = instance.GetCurrentProcessId();
            Assert.AreNotEqual(remotePid, Environment.ProcessId);
        }

        [Test]
        public void TwoRemoteInstancesAreNotEqual()
        {
            var instance1 = GetRemoteInstance();
            var instance2 = GetRemoteInstance();
            Assert.AreNotEqual(instance1.Identifier, instance2.Identifier);
        }

        [Test]
        public void SameInstanceIsUsedInTwoCalls()
        {
            var instance1 = GetRemoteInstance();
            long a = instance1.Identifier;
            long b = instance1.Identifier;
            Assert.AreEqual(a, b);
        }

        private MarshallableClass GetRemoteInstance()
        {
            return _client.CreateRemoteInstance<MarshallableClass>();
        }
    }
}
