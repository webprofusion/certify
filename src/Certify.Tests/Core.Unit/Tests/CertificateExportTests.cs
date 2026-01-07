using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class CertificateExportTests
    {
        private const string TEST_PASSWORD = "";//"test123";
        private byte[] _testPfxData;

        [TestInitialize]
        public void Setup()
        {
            // Load test PFX certificate
            var pfxPath = "Assets/dummycert.pfx";
            Assert.IsTrue(File.Exists(pfxPath), $"Test PFX file not found: {pfxPath}");
            _testPfxData = File.ReadAllBytes(pfxPath);
        }

        [TestMethod, Description("Test export as PFX format returns original bytes")]
        public void TestGetCertificateExportResult_Pfx()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pfx",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Result);
            Assert.IsNotEmpty(result.Result);
            CollectionAssert.AreEqual(_testPfxData, result.Result, "PFX export should return original bytes");
        }

        [TestMethod, Description("Test export private key only")]
        public void TestGetCertificateExportResult_PrivateKeyOnly()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pem_key",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Result);
            Assert.IsNotEmpty(result.Result);

            var pemString = System.Text.Encoding.ASCII.GetString(result.Result);
            Assert.Contains("PRIVATE KEY-----", pemString, "Should contain private key marker");
            Assert.DoesNotContain("-----BEGIN CERTIFICATE-----", pemString, "Should not contain certificate");
        }

        [TestMethod, Description("Test export fullchain (end entity + intermediates)")]
        public void TestGetCertificateExportResult_FullChain()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pem_fullchain",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Result);
            Assert.IsNotEmpty(result.Result);

            var pemString = System.Text.Encoding.ASCII.GetString(result.Result);
            Assert.Contains("-----BEGIN CERTIFICATE-----", pemString, "Should contain certificate");
            Assert.DoesNotContain("PRIVATE KEY-----", pemString, "Should not contain private key");

            var certCount = CountPemCertificates(pemString);
            Assert.IsGreaterThanOrEqualTo(1, certCount, "Should contain at least end entity certificate");
        }

        [TestMethod, Description("Test export fullchain with private key")]
        public void TestGetCertificateExportResult_FullChainWithKey()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pem_fullchain_key",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Result);
            Assert.IsNotEmpty(result.Result);

            var pemString = System.Text.Encoding.ASCII.GetString(result.Result);
            Assert.Contains("PRIVATE KEY-----", pemString, "Should contain private key");
            Assert.Contains("-----BEGIN CERTIFICATE-----", pemString, "Should contain certificate");

            // Private key should come before certificate
            var keyIndex = pemString.IndexOf("PRIVATE KEY-----");
            var certIndex = pemString.IndexOf("-----BEGIN CERTIFICATE-----");
            Assert.IsGreaterThanOrEqualTo(0, keyIndex, "Should contain private key");
            Assert.IsGreaterThanOrEqualTo(0, certIndex, "Should contain certificate");
            Assert.IsLessThan(certIndex, keyIndex, "Private key should appear before certificate");
        }

        [TestMethod, Description("Test export fullchain with root certificate")]
        public void TestGetCertificateExportResult_FullChainWithRoot()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pem_fullchain_root",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Result);
            Assert.IsNotEmpty(result.Result);

            var pemString = System.Text.Encoding.ASCII.GetString(result.Result);
            Assert.Contains("-----BEGIN CERTIFICATE-----", pemString, "Should contain certificate");
            Assert.DoesNotContain("PRIVATE KEY-----", pemString, "Should not contain private key");

            var certCount = CountPemCertificates(pemString);
            Assert.IsGreaterThanOrEqualTo(1, certCount, "Should contain at least end entity certificate");
        }

        [TestMethod, Description("Test export fullchain with root and private key")]
        public void TestGetCertificateExportResult_FullChainRootAndKey()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pem_fullchain_root_key",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Result);
            Assert.IsNotEmpty(result.Result);

            var pemString = System.Text.Encoding.ASCII.GetString(result.Result);
            Assert.Contains("PRIVATE KEY-----", pemString, "Should contain private key");
            Assert.Contains("-----BEGIN CERTIFICATE-----", pemString, "Should contain certificate");

            var certCount = CountPemCertificates(pemString);
            Assert.IsGreaterThanOrEqualTo(1, certCount, "Should contain at least end entity certificate");

            // Private key should come before certificate
            var keyIndex = pemString.IndexOf("PRIVATE KEY-----");
            var certIndex = pemString.IndexOf("-----BEGIN CERTIFICATE-----");
            Assert.IsLessThan(certIndex, keyIndex, "Private key should appear before certificate");
        }

        [TestMethod, Description("Test export with invalid format returns error")]
        public void TestGetCertificateExportResult_InvalidFormat()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "invalid_format",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess, "Invalid format should fail");
            Assert.IsTrue(result.Message.Contains("no files where selected") || result.Message.Contains("export"),
                "Error message should indicate export issue");
        }

        [TestMethod, Description("Test export with empty format returns error")]
        public void TestGetCertificateExportResult_EmptyFormat()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsNotNull(result);
            // Empty format should result in no export (result.Result.Length == 0)
            Assert.IsFalse(result.IsSuccess, "Empty format should fail");
        }

        [TestMethod, Description("Test export with strictExport flag enabled")]
        public void TestGetCertificateExportResult_StrictExport()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pem_fullchain",
                strictExport: true,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Result);
            Assert.IsNotEmpty(result.Result);

            var pemString = System.Text.Encoding.ASCII.GetString(result.Result);
            Assert.Contains("-----BEGIN CERTIFICATE-----", pemString, "Should contain certificate");

            var certCount = CountPemCertificates(pemString);
            Assert.IsGreaterThanOrEqualTo(2, certCount, "Should contain at least end entity certificate and 1 or more intermediates");

        }

        [TestMethod, Description("Test all supported export formats return non-empty results")]
        public void TestGetCertificateExportResult_AllFormatsValid()
        {
            var formats = new[]
            {
                "pfx",
                "pem_key",
                "pem_fullchain",
                "pem_fullchain_key",
                "pem_fullchain_root",
                "pem_fullchain_root_key"
            };

            foreach (var format in formats)
            {
                // Act
                var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                    format,
                    strictExport: false,
                    _testPfxData,
                    TEST_PASSWORD
                );

                // Assert
                Assert.IsNotNull(result, $"Result should not be null for format: {format}");
                Assert.IsTrue(result.IsSuccess, $"Export should succeed for format: {format}");
                Assert.IsNotNull(result.Result, $"Result data should not be null for format: {format}");
                Assert.IsNotEmpty(result.Result, $"Result should contain data for format: {format}");
            }
        }

        [TestMethod, Description("Test PFX export with strictExport has no effect on PFX format")]
        public void TestGetCertificateExportResult_PfxIgnoresStrictExport()
        {
            // Act - test both strict and non-strict
            var resultNonStrict = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pfx",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            var resultStrict = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pfx",
                strictExport: true,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert - both should return identical results
            Assert.IsTrue(resultNonStrict.IsSuccess);
            Assert.IsTrue(resultStrict.IsSuccess);
            CollectionAssert.AreEqual(resultNonStrict.Result, resultStrict.Result,
                "PFX export should be identical regardless of strictExport flag");
        }

        [TestMethod, Description("Test private key format contains only key, no certificates")]
        public void TestGetCertificateExportResult_PrivateKeyNoLeakage()
        {
            // Act
            var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                "pem_key",
                strictExport: false,
                _testPfxData,
                TEST_PASSWORD
            );

            // Assert
            Assert.IsTrue(result.IsSuccess);
            var pemString = System.Text.Encoding.ASCII.GetString(result.Result);

            // Should have private key markers
            Assert.Contains(
"PRIVATE KEY-----",
                pemString, "Should contain private key marker"
            );

            // Should NOT have any certificate markers
            Assert.DoesNotContain(
"-----BEGIN CERTIFICATE-----",
                pemString, "Private key export should not contain certificates"
            );
        }

        [TestMethod, Description("Test export formats produce parseable PEM output")]
        public void TestGetCertificateExportResult_ValidPemFormat()
        {
            var pemFormats = new[]
            {
                "pem_key",
                "pem_fullchain",
                "pem_fullchain_key",
                "pem_fullchain_root",
                "pem_fullchain_root_key"
            };

            foreach (var format in pemFormats)
            {
                // Act
                var result = Certify.Management.CertifyManager.GetCertificateExportResult(
                    format,
                    strictExport: false,
                    _testPfxData,
                    TEST_PASSWORD
                );

                // Assert
                Assert.IsTrue(result.IsSuccess, $"Format {format} should succeed");
                var pemString = System.Text.Encoding.ASCII.GetString(result.Result);

                // All PEM formats should have proper BEGIN/END markers
                Assert.Contains("-----BEGIN", pemString, $"Format {format} should contain BEGIN marker");
                Assert.Contains("-----END", pemString, $"Format {format} should contain END marker");

                // Check for balanced markers
                var beginCount = pemString.Split(new[] { "-----BEGIN" }, StringSplitOptions.None).Length - 1;
                var endCount = pemString.Split(new[] { "-----END" }, StringSplitOptions.None).Length - 1;
                Assert.AreEqual(beginCount, endCount, $"Format {format} should have balanced BEGIN/END markers");
            }
        }

        /// <summary>
        /// Helper to count PEM certificates in a string
        /// </summary>
        private static int CountPemCertificates(string pem)
        {
            if (string.IsNullOrWhiteSpace(pem))
            {
                return 0;
            }

            return pem.Split(new[] { "-----BEGIN CERTIFICATE-----" }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
