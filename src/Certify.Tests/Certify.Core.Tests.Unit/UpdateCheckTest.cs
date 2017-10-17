using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class UpdateCheckTest
    {
        [TestMethod]
        public void TestUpdateCheck()
        {
            var updateChecker = new Certify.Management.Util();
            var result = updateChecker.CheckForUpdates("2.0.1").Result;

            // current version is older than newer version
            Assert.IsTrue(result.IsNewerVersion);

            result = updateChecker.CheckForUpdates("5.1.1").Result;

            // current version is newer than update version
            Assert.IsFalse(result.IsNewerVersion);
        }

        [TestMethod]
        public void CheckMultipleVersions()
        {
            Certify.Models.AppVersion v2_0_13 = new Models.AppVersion() { Major = 2, Minor = 0, Patch = 13 };

            Certify.Models.AppVersion v2_1_1 = new Models.AppVersion() { Major = 2, Minor = 1, Patch = 1 };

            Certify.Models.AppVersion v2_1_2 = new Models.AppVersion() { Major = 2, Minor = 1, Patch = 2 };

            Certify.Models.AppVersion v2_2_2 = new Models.AppVersion() { Major = 2, Minor = 2, Patch = 2 };

            Certify.Models.AppVersion v3_1_1 = new Models.AppVersion() { Major = 3, Minor = 1, Patch = 2 };

            var isNewer = Certify.Models.AppVersion.IsOtherVersionNewer(v2_1_1, v2_0_13);
            Assert.IsFalse(isNewer, "Older version is not newer than current");

            isNewer = Certify.Models.AppVersion.IsOtherVersionNewer(v2_1_1, v2_1_2);
            Assert.IsTrue(isNewer, "Higher patch version is newer than current");

            isNewer = Certify.Models.AppVersion.IsOtherVersionNewer(v2_1_2, v2_1_1);
            Assert.IsFalse(isNewer, "Lower patch version is not newer");

            isNewer = Certify.Models.AppVersion.IsOtherVersionNewer(v2_1_2, v2_2_2);
            Assert.IsTrue(isNewer, "Higher minor version is newer");

            isNewer = Certify.Models.AppVersion.IsOtherVersionNewer(v2_1_1, v3_1_1);
            Assert.IsTrue(isNewer, "Higher major version is newer");

            Assert.IsFalse(Certify.Models.AppVersion.IsOtherVersionNewer(v2_1_1, v2_1_1), "Same version is not newer");
        }
    }
}