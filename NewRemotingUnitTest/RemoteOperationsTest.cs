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
    public class RemoteOperationsTest
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
            var instance = CreateRemoteInstance();
            Assert.IsNotNull(instance);
            Assert.That(Client.IsRemoteProxy(instance));
        }

        [Test]
        public void RemoteInstanceCanBeCalled()
        {
            var instance = CreateRemoteInstance();
            int remotePid = instance.GetCurrentProcessId();
            Assert.AreNotEqual(remotePid, Environment.ProcessId);
        }

        [Test]
        public void TwoRemoteInstancesAreNotEqual()
        {
            var instance1 = CreateRemoteInstance();
            var instance2 = CreateRemoteInstance();
            Assert.AreNotEqual(instance1.Identifier, instance2.Identifier);
        }

        [Test]
        public void SameInstanceIsUsedInTwoCalls()
        {
            var instance1 = CreateRemoteInstance();
            long a = instance1.Identifier;
            long b = instance1.Identifier;
            Assert.AreEqual(a, b);
        }

        [Test]
        public void CanCreateInstanceWithNonDefaultCtor()
        {
            var instance = _client.CreateRemoteInstance<MarshallableClass>(23);
            Assert.AreEqual(23, instance.Identifier);
        }

        [Test]
        public void CanMarshalSystemType()
        {
            var instance = CreateRemoteInstance();
            Assert.AreEqual("System.String", instance.GetTypeName(typeof(System.String)));
        }

        [Test]
        public void CanMarshalNullReference()
        {
            var instance = CreateRemoteInstance();
            instance.RegisterCallback(null);
        }

        private MarshallableClass CreateRemoteInstance()
        {
            return _client.CreateRemoteInstance<MarshallableClass>();
        }
    }
}
