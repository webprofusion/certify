using System;
using System.Collections.Generic;
using System.IO;
using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Full certificate cleanup removes old certificate files from the asset folders. A subscription rewrites one file in
    /// place on each update, which keeps its original creation time, so a file in use must not be removed for its age
    /// </summary>
    [TestClass]
    public class CertificateFileCleanupTests
    {
        private string _assetPath;

        [TestInitialize]
        public void Setup()
        {
            _assetPath = Path.Combine(Path.GetTempPath(), "certify-cleanup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_assetPath, "external"));
        }

        [TestCleanup]
        public void Cleanup() => Directory.Delete(_assetPath, recursive: true);

        private string CreateFile(string relativePath, DateTime createdAt)
        {
            var path = Path.Combine(_assetPath, relativePath);
            File.WriteAllText(path, "certificate");
            File.SetCreationTime(path, createdAt);
            return path;
        }

        private void Clean(params string[] inUse)
        {
            CertifyManager.DeleteOldCertificateFiles(_assetPath, new List<string> { ".pfx", ".key", ".crt", ".pem" }, inUse, DateTimeOffset.UtcNow.AddMonths(-12));
        }

        [TestMethod, Description("An old certificate file no managed certificate uses is removed")]
        public void OldUnusedFileIsRemoved()
        {
            var unused = CreateFile("old.pfx", DateTime.Now.AddYears(-2));

            Clean();

            Assert.IsFalse(File.Exists(unused));
        }

        [TestMethod, Description("A recent certificate file is kept")]
        public void RecentFileIsKept()
        {
            var recent = CreateFile("recent.pfx", DateTime.Now.AddMonths(-1));

            Clean();

            Assert.IsTrue(File.Exists(recent));
        }

        [TestMethod, Description("An old subscription asset still in use is kept, however long ago it was first created")]
        public void OldFileInUseIsKept()
        {
            var subscriptionAsset = CreateFile(Path.Combine("external", "subscriber-item.pfx"), DateTime.Now.AddYears(-2));

            Clean(subscriptionAsset);

            Assert.IsTrue(File.Exists(subscriptionAsset));
        }

        [TestMethod, Description("Files alongside an in use certificate with the same name are kept, matching the path regardless of case")]
        public void CompanionFilesOfCertificateInUseAreKept()
        {
            var pfx = CreateFile("cert.pfx", DateTime.Now.AddYears(-2));
            var key = CreateFile("cert.key", DateTime.Now.AddYears(-2));

            Clean(pfx.ToUpperInvariant());

            Assert.IsTrue(File.Exists(pfx));
            Assert.IsTrue(File.Exists(key));
        }
    }
}
