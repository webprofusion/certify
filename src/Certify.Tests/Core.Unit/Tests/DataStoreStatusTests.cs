using System;
using Certify.Models.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class DataStoreStatusTests
    {
        [TestMethod]
        public void DataStoreStatus_DefaultState_IsNotConnected()
        {
            var status = new DataStoreStatus();

            Assert.IsFalse(status.IsConnected);
            Assert.IsFalse(status.IsDegradedMode);
            Assert.AreEqual(string.Empty, status.StatusMessage);
            Assert.IsNull(status.DataStoreId);
            Assert.IsNull(status.DataStoreType);
            Assert.IsNull(status.LastSuccessfulConnection);
            Assert.IsNull(status.LastErrorTime);
            Assert.IsNull(status.LastErrorMessage);
            Assert.AreEqual(0, status.ConsecutiveFailures);
        }

        [TestMethod]
        public void DataStoreStatus_ConnectedState_HasCorrectProperties()
        {
            var status = new DataStoreStatus
            {
                IsConnected = true,
                IsDegradedMode = false,
                StatusMessage = "Data store connected and operational.",
                DataStoreId = "test-store",
                DataStoreType = "sqlite",
                LastSuccessfulConnection = DateTimeOffset.UtcNow
            };

            Assert.IsTrue(status.IsConnected);
            Assert.IsFalse(status.IsDegradedMode);
            Assert.AreEqual("Data store connected and operational.", status.StatusMessage);
            Assert.AreEqual("test-store", status.DataStoreId);
            Assert.AreEqual("sqlite", status.DataStoreType);
            Assert.IsNotNull(status.LastSuccessfulConnection);
        }

        [TestMethod]
        public void DataStoreStatus_DegradedModeState_HasCorrectProperties()
        {
            var errorMessage = "Connection refused";
            var status = new DataStoreStatus
            {
                IsConnected = false,
                IsDegradedMode = true,
                StatusMessage = $"Service running in degraded mode. Data store unavailable: {errorMessage}",
                DataStoreId = "postgres-store",
                DataStoreType = "postgres",
                LastErrorTime = DateTimeOffset.UtcNow,
                LastErrorMessage = errorMessage,
                ConsecutiveFailures = 3
            };

            Assert.IsFalse(status.IsConnected);
            Assert.IsTrue(status.IsDegradedMode);
            Assert.IsTrue(status.StatusMessage.Contains("degraded mode"));
            Assert.AreEqual("postgres-store", status.DataStoreId);
            Assert.AreEqual("postgres", status.DataStoreType);
            Assert.IsNotNull(status.LastErrorTime);
            Assert.AreEqual(errorMessage, status.LastErrorMessage);
            Assert.AreEqual(3, status.ConsecutiveFailures);
        }

        [TestMethod]
        public void DataStoreStatus_IncrementFailures_TracksCorrectly()
        {
            var status = new DataStoreStatus();

            Assert.AreEqual(0, status.ConsecutiveFailures);

            status.ConsecutiveFailures++;
            Assert.AreEqual(1, status.ConsecutiveFailures);

            status.ConsecutiveFailures++;
            Assert.AreEqual(2, status.ConsecutiveFailures);

            // Simulate successful connection - reset failures
            status.IsConnected = true;
            status.IsDegradedMode = false;
            status.ConsecutiveFailures = 0;
            Assert.AreEqual(0, status.ConsecutiveFailures);
        }
    }
}
