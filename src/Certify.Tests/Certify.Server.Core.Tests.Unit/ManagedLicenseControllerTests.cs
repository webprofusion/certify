using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Service.Controllers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Server.Core.Tests.Unit
{
    [TestClass]
    public class ManagedLicenseControllerTests
    {
        private Mock<ICertifyManager> _mockCertifyManager;
        private ManagedLicenseController _controller;

        [TestInitialize]
        public void Setup()
        {
            _mockCertifyManager = new Mock<ICertifyManager>();
            _controller = new ManagedLicenseController(_mockCertifyManager.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _mockCertifyManager = null;
            _controller = null;
        }

        #region ActivateManagedLicense Tests

        [TestMethod]
        public async Task ActivateManagedLicense_WithValidParameters_ReturnsSuccessResult()
        {
            // Arrange
            var id = "test-license-id";
            var instanceId = "test-instance-id";
            var expectedResult = new ActionResult("License activated successfully", true);

            _mockCertifyManager
                .Setup(x => x.ActivateManagedLicense(id, instanceId))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.ActivateManagedLicense(id, instanceId);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("License activated successfully", result.Message);
            _mockCertifyManager.Verify(x => x.ActivateManagedLicense(id, instanceId), Times.Once);
        }

        [TestMethod]
        public async Task ActivateManagedLicense_WithNullId_ReturnsErrorResult()
        {
            // Arrange
            string id = null;
            var instanceId = "test-instance-id";
            var expectedResult = new ActionResult("Invalid license ID", false);

            _mockCertifyManager
                .Setup(x => x.ActivateManagedLicense(id, instanceId))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.ActivateManagedLicense(id, instanceId);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess);
            _mockCertifyManager.Verify(x => x.ActivateManagedLicense(id, instanceId), Times.Once);
        }

        [TestMethod]
        public async Task ActivateManagedLicense_WhenManagerThrowsException_PropagatesException()
        {
            // Arrange
            var id = "test-license-id";
            var instanceId = "test-instance-id";

            _mockCertifyManager
                .Setup(x => x.ActivateManagedLicense(id, instanceId))
                .ThrowsAsync(new InvalidOperationException("Test exception"));

            // Act & Assert
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _controller.ActivateManagedLicense(id, instanceId));
        }

        #endregion

        #region RemoveManagedLicense Tests

        [TestMethod]
        public async Task RemoveManagedLicense_WithValidId_ReturnsSuccessResult()
        {
            // Arrange
            var id = "test-license-id";
            var expectedResult = new ActionResult("License removed successfully", true);

            _mockCertifyManager
                .Setup(x => x.RemoveManagedLicenses(id))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.RemoveManagedLicense(id);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("License removed successfully", result.Message);
            _mockCertifyManager.Verify(x => x.RemoveManagedLicenses(id), Times.Once);
        }

        [TestMethod]
        public async Task RemoveManagedLicense_WithNullId_ReturnsErrorResult()
        {
            // Arrange
            string id = null;
            var expectedResult = new ActionResult("Invalid license ID", false);

            _mockCertifyManager
                .Setup(x => x.RemoveManagedLicenses(id))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.RemoveManagedLicense(id);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess);
            _mockCertifyManager.Verify(x => x.RemoveManagedLicenses(id), Times.Once);
        }

        #endregion

        #region GetManagedLicenses Tests

        [TestMethod]
        public async Task GetManagedLicenses_ReturnsLicenseCollection()
        {
            // Arrange
            var expectedLicenses = new List<ManagedLicense>
            {
                new ManagedLicense("test1@example.com", "key1", "ProductType1")
                {
                    Id = "license1",
                    Title = "Test License 1"
                },
                new ManagedLicense("test2@example.com", "key2", "ProductType2")
                {
                    Id = "license2",
                    Title = "Test License 2"
                }
            };

            _mockCertifyManager
                .Setup(x => x.GetManagedLicenses())
                .ReturnsAsync(expectedLicenses);

            // Act
            var result = await _controller.GetManagedLicenses();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            _mockCertifyManager.Verify(x => x.GetManagedLicenses(), Times.Once);
        }

        [TestMethod]
        public async Task GetManagedLicenses_ReturnsEmptyCollection_WhenNoLicenses()
        {
            // Arrange
            var expectedLicenses = new List<ManagedLicense>();

            _mockCertifyManager
                .Setup(x => x.GetManagedLicenses())
                .ReturnsAsync(expectedLicenses);

            // Act
            var result = await _controller.GetManagedLicenses();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
            _mockCertifyManager.Verify(x => x.GetManagedLicenses(), Times.Once);
        }

        #endregion

        #region DeactivateManagedLicense Tests

        [TestMethod]
        public async Task DeactivateManagedLicense_WithValidId_ReturnsSuccessResult()
        {
            // Arrange
            var id = "test-license-id";
            var expectedResult = new ActionResult("License deactivated successfully", true);

            _mockCertifyManager
                .Setup(x => x.DeactivateManagedLicense(id, null))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.DeactivateManagedLicense(id);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("License deactivated successfully", result.Message);
            _mockCertifyManager.Verify(x => x.DeactivateManagedLicense(id, null), Times.Once);
        }

        [TestMethod]
        public async Task DeactivateManagedLicense_WithNullId_ReturnsErrorResult()
        {
            // Arrange
            string id = null;
            var expectedResult = new ActionResult("Invalid license ID", false);

            _mockCertifyManager
                .Setup(x => x.DeactivateManagedLicense(id, null))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.DeactivateManagedLicense(id);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess);
            _mockCertifyManager.Verify(x => x.DeactivateManagedLicense(id, null), Times.Once);
        }

        #endregion

        #region AddManagedLicense Tests

        [TestMethod]
        public async Task AddManagedLicense_WithValidLicense_ReturnsSuccessResult()
        {
            // Arrange
            var license = new ManagedLicense("test@example.com", "test-key", "ProductType1")
            {
                Id = "test-license-id",
                Title = "Test License"
            };
            var expectedResult = new ActionResult("License added successfully", true);

            _mockCertifyManager
                .Setup(x => x.AddManagedLicense(license))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.AddManagedLicense(license);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("License added successfully", result.Message);
            _mockCertifyManager.Verify(x => x.AddManagedLicense(license), Times.Once);
        }

        [TestMethod]
        public async Task AddManagedLicense_WithNullLicense_ReturnsErrorResult()
        {
            // Arrange
            ManagedLicense license = null;
            var expectedResult = new ActionResult("Invalid license data", false);

            _mockCertifyManager
                .Setup(x => x.AddManagedLicense(license))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.AddManagedLicense(license);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess);
            _mockCertifyManager.Verify(x => x.AddManagedLicense(license), Times.Once);
        }

        #endregion

        #region GetManagedLicenseStatus Tests

        [TestMethod]
        public async Task GetManagedLicenseStatus_WithValidId_ReturnsStatusResult()
        {
            // Arrange
            var id = "test-license-id";
            var expectedResult = new ActionResult("License is active", true)
            {
                Result = new { Status = "Active", ExpiryDate = DateTime.UtcNow.AddDays(30) }
            };

            _mockCertifyManager
                .Setup(x => x.GetManagedLicenseStatus(id))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.GetManagedLicenseStatus(id);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("License is active", result.Message);
            Assert.IsNotNull(result.Result);
            _mockCertifyManager.Verify(x => x.GetManagedLicenseStatus(id), Times.Once);
        }

        [TestMethod]
        public async Task GetManagedLicenseStatus_WithInvalidId_ReturnsErrorResult()
        {
            // Arrange
            var id = "invalid-license-id";
            var expectedResult = new ActionResult("License not found", false);

            _mockCertifyManager
                .Setup(x => x.GetManagedLicenseStatus(id))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.GetManagedLicenseStatus(id);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("License not found", result.Message);
            _mockCertifyManager.Verify(x => x.GetManagedLicenseStatus(id), Times.Once);
        }

        #endregion

        #region UpdateManagedLicense Tests

        [TestMethod]
        public async Task UpdateManagedLicense_WithValidLicense_ReturnsSuccessResult()
        {
            // Arrange
            var license = new ManagedLicense("updated@example.com", "updated-key", "ProductType1")
            {
                Id = "test-license-id",
                Title = "Updated Test License"
            };
            var expectedResult = new ActionResult("License updated successfully", true);

            _mockCertifyManager
                .Setup(x => x.UpdateManagedLicense(license))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.UpdateManagedLicense(license);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("License updated successfully", result.Message);
            _mockCertifyManager.Verify(x => x.UpdateManagedLicense(license), Times.Once);
        }

        [TestMethod]
        public async Task UpdateManagedLicense_WithNullLicense_ReturnsErrorResult()
        {
            // Arrange
            ManagedLicense license = null;
            var expectedResult = new ActionResult("Invalid license data", false);

            _mockCertifyManager
                .Setup(x => x.UpdateManagedLicense(license))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.UpdateManagedLicense(license);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess);
            _mockCertifyManager.Verify(x => x.UpdateManagedLicense(license), Times.Once);
        }

        [TestMethod]
        public async Task UpdateManagedLicense_WhenManagerThrowsException_PropagatesException()
        {
            // Arrange
            var license = new ManagedLicense("test@example.com", "test-key", "ProductType1");

            _mockCertifyManager
                .Setup(x => x.UpdateManagedLicense(license))
                .ThrowsAsync(new InvalidOperationException("Update failed"));

            // Act & Assert
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => _controller.UpdateManagedLicense(license));
        }

        #endregion

        #region Integration Tests

        [TestMethod]
        public async Task Controller_CallsCorrectManagerMethods_ForAllOperations()
        {
            // Arrange
            var testLicense = new ManagedLicense("test@example.com", "test-key", "ProductType1")
            {
                Id = "test-id",
                Title = "Test License"
            };

            var successResult = new ActionResult("Success", true);
            var licenseList = new List<ManagedLicense> { testLicense };

            _mockCertifyManager.Setup(x => x.ActivateManagedLicense(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(successResult);
            _mockCertifyManager.Setup(x => x.DeactivateManagedLicense(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(successResult);
            _mockCertifyManager.Setup(x => x.AddManagedLicense(It.IsAny<ManagedLicense>())).ReturnsAsync(successResult);
            _mockCertifyManager.Setup(x => x.UpdateManagedLicense(It.IsAny<ManagedLicense>())).ReturnsAsync(successResult);
            _mockCertifyManager.Setup(x => x.RemoveManagedLicenses(It.IsAny<string>())).ReturnsAsync(successResult);
            _mockCertifyManager.Setup(x => x.GetManagedLicenseStatus(It.IsAny<string>())).ReturnsAsync(successResult);
            _mockCertifyManager.Setup(x => x.GetManagedLicenses()).ReturnsAsync(licenseList);

            // Act
            await _controller.ActivateManagedLicense("test-id", "instance-id");
            await _controller.DeactivateManagedLicense("test-id");
            await _controller.AddManagedLicense(testLicense);
            await _controller.UpdateManagedLicense(testLicense);
            await _controller.RemoveManagedLicense("test-id");
            await _controller.GetManagedLicenseStatus("test-id");
            await _controller.GetManagedLicenses();

            // Assert
            _mockCertifyManager.Verify(x => x.ActivateManagedLicense("test-id", "instance-id"), Times.Once);
            _mockCertifyManager.Verify(x => x.DeactivateManagedLicense("test-id", null), Times.Once);
            _mockCertifyManager.Verify(x => x.AddManagedLicense(testLicense), Times.Once);
            _mockCertifyManager.Verify(x => x.UpdateManagedLicense(testLicense), Times.Once);
            _mockCertifyManager.Verify(x => x.RemoveManagedLicenses("test-id"), Times.Once);
            _mockCertifyManager.Verify(x => x.GetManagedLicenseStatus("test-id"), Times.Once);
            _mockCertifyManager.Verify(x => x.GetManagedLicenses(), Times.Once);
        }

        #endregion

        #region Controller-Specific Tests

        [TestMethod]
        public void Constructor_WithNullCertifyManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => new ManagedLicenseController(null));
        }

        [TestMethod]
        public async Task ActivateManagedLicense_WithEmptyStrings_PassesToManager()
        {
            // Arrange
            var id = "";
            var instanceId = "";
            var expectedResult = new ActionResult("Error", false);

            _mockCertifyManager
                .Setup(x => x.ActivateManagedLicense(id, instanceId))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.ActivateManagedLicense(id, instanceId);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess);
            _mockCertifyManager.Verify(x => x.ActivateManagedLicense(id, instanceId), Times.Once);
        }

        [TestMethod]
        public async Task GetManagedLicenses_WhenManagerThrowsException_PropagatesException()
        {
            // Arrange
            _mockCertifyManager
                .Setup(x => x.GetManagedLicenses())
                .ThrowsAsync(new InvalidOperationException("Database connection failed"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _controller.GetManagedLicenses());
        }

        #endregion
    }
}
