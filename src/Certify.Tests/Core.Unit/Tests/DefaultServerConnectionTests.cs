using System;
using Certify.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// The default (local) server connection is derived from the service config rather than saved by
    /// the user, so it has to select the same transport the service publishes.
    /// </summary>
    [TestClass]
    public class DefaultServerConnectionTests
    {
        private string _originalTransportOverride;

        [TestInitialize]
        public void Setup()
        {
            // the env var override would otherwise decide the result on a machine which has it set
            _originalTransportOverride = Environment.GetEnvironmentVariable(NamedPipeConnection.TransportEnvVariable);
            Environment.SetEnvironmentVariable(NamedPipeConnection.TransportEnvVariable, null);
        }

        [TestCleanup]
        public void Cleanup() => Environment.SetEnvironmentVariable(NamedPipeConnection.TransportEnvVariable, _originalTransportOverride);

        [TestMethod, Description("Default connection uses http when the service publishes http")]
        public void DefaultConnectionUsesHttpTransport()
        {
            var connection = new ServerConnection(new ServiceConfig { Transport = NamedPipeConnection.HttpMode });

            Assert.IsFalse(connection.UseNamedPipe, "default connection should use the http endpoint");
            Assert.AreEqual("direct", connection.Mode);
        }

        [TestMethod, Description("Default connection uses the named pipe when the service publishes the named pipe")]
        public void DefaultConnectionUsesNamedPipeTransport()
        {
            var connection = new ServerConnection(new ServiceConfig { Transport = NamedPipeConnection.ConnectionMode });

            if (NamedPipeConnection.IsPlatformSupported)
            {
                Assert.IsTrue(connection.UseNamedPipe, "default connection should follow the service onto the named pipe");
                Assert.AreEqual(NamedPipeConnection.ConnectionMode, connection.Mode);
            }
            else
            {
                // the service falls back to http on platforms without named pipe support
                Assert.IsFalse(connection.UseNamedPipe, "named pipe is not available on this platform");
                Assert.AreEqual("direct", connection.Mode);
            }
        }

        [TestMethod, Description("Default connection falls back to http for an unrecognised transport")]
        public void DefaultConnectionFallsBackToHttp()
        {
            var connection = new ServerConnection(new ServiceConfig { Transport = "something-else" });

            Assert.IsFalse(connection.UseNamedPipe, "an unrecognised transport should fail safe to http");
        }

        [TestMethod, Description("Default connection honours the transport environment override")]
        public void DefaultConnectionHonoursEnvironmentOverride()
        {
            Environment.SetEnvironmentVariable(NamedPipeConnection.TransportEnvVariable, NamedPipeConnection.ConnectionMode);

            var connection = new ServerConnection(new ServiceConfig { Transport = NamedPipeConnection.HttpMode });

            Assert.AreEqual(NamedPipeConnection.IsPlatformSupported, connection.UseNamedPipe, "override should apply to the client as well as the service");
        }
    }
}
