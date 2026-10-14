using System;
using System.Collections.ObjectModel;
using System.Linq;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for restricting changes made locally on an instance administered through the hub
    /// </summary>
    [TestClass]
    public class LocalChangeRestrictionTests
    {
        private static ManagedCertificate CreateItem(string name, string taskName) => new ManagedCertificate
        {
            Id = "item-1",
            Name = name,
            PostRequestTasks = new ObservableCollection<DeploymentTaskConfig> { new DeploymentTaskConfig { Id = "task-1", TaskName = taskName } }
        };

        /// <summary>
        /// Run a test with a copy of the current settings, so changes made by the test are discarded
        /// </summary>
        private static void WithSettings(LocalChangeRestriction restriction, Action test)
        {
            var saved = CoreAppSettings.Current;
            CoreAppSettings.Current = JsonConvert.DeserializeObject<CoreAppSettings>(JsonConvert.SerializeObject(saved))!;

            try
            {
                CoreAppSettings.Current.LocalChangeRestriction = restriction;
                test();
            }
            finally
            {
                CoreAppSettings.Current = saved;
            }
        }

        [TestMethod, Description("With no restriction a local update is stored as given")]
        public void TestUpdateUnrestricted()
        {
            var update = CreateItem("Updated", "Updated Task");

            var result = LocalChangePolicy.ResolveManagedCertificateUpdate(LocalChangeRestriction.None, null, update);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreSame(update, result.Result);
        }

        [TestMethod, Description("When only deployment tasks can change locally, other changes in the update are not kept")]
        public void TestUpdateDeploymentTasksOnly()
        {
            var stored = CreateItem("Stored", "Stored Task");
            var update = CreateItem("Updated", "Updated Task");
            update.PreRequestTasks = new ObservableCollection<DeploymentTaskConfig> { new DeploymentTaskConfig { Id = "task-2" } };

            var result = LocalChangePolicy.ResolveManagedCertificateUpdate(LocalChangeRestriction.DeploymentTasksOnly, stored, update);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("Stored", result.Result!.Name);
            Assert.AreEqual("Updated Task", result.Result.PostRequestTasks![0].TaskName);
            Assert.AreEqual("task-2", result.Result.PreRequestTasks![0].Id);
        }

        [TestMethod, Description("When only deployment tasks can change locally, a new item cannot be added")]
        public void TestAddDeploymentTasksOnly()
        {
            var result = LocalChangePolicy.ResolveManagedCertificateUpdate(LocalChangeRestriction.DeploymentTasksOnly, null, CreateItem("New", "Task"));

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(LocalChangePolicy.DeploymentTasksOnlyMessage, result.Message);
        }

        [TestMethod, Description("When the instance is read only, no local update is allowed, including deployment tasks")]
        public void TestUpdateReadOnly()
        {
            var result = LocalChangePolicy.ResolveManagedCertificateUpdate(LocalChangeRestriction.ReadOnly, CreateItem("Stored", "Stored Task"), CreateItem("Updated", "Updated Task"));

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(LocalChangePolicy.ReadOnlyMessage, result.Message);
        }

        /// <summary>
        /// The current preferences as a client would send them back, round tripped so serialization does not cause differences
        /// </summary>
        private static Preferences GetClientPreferences() => JsonConvert.DeserializeObject<Preferences>(JsonConvert.SerializeObject(SettingsManager.GetCurrentPreferences()))!;

        [TestMethod, Description("The restriction can only be changed from the hub, a local preferences update keeps the current value")]
        public void TestLocalPreferencesUpdateKeepsRestriction()
        {
            WithSettings(LocalChangeRestriction.None, () =>
            {
                var result = SettingsManager.FromLocalPreferences(new Preferences { LocalChangeRestriction = LocalChangeRestriction.ReadOnly, EnableStatusReporting = false });

                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual(LocalChangeRestriction.None, CoreAppSettings.Current.LocalChangeRestriction);
                Assert.IsFalse(CoreAppSettings.Current.EnableStatusReporting, "Other settings are still applied from a local update");

                var prefs = new Preferences();
                SettingsManager.ApplyHubSettingsUpdate(prefs, new Preferences { LocalChangeRestriction = LocalChangeRestriction.ReadOnly });
                SettingsManager.FromPreferences(prefs);

                Assert.AreEqual(LocalChangeRestriction.ReadOnly, CoreAppSettings.Current.LocalChangeRestriction);
            });
        }

        [TestMethod, Description("While restricted, a local preferences update which changes a general setting is refused, whether or not the hub manages it")]
        [DataRow(LocalChangeRestriction.DeploymentTasksOnly)]
        [DataRow(LocalChangeRestriction.ReadOnly)]
        public void TestRestrictedPreferencesRefuseGeneralSetting(LocalChangeRestriction restriction)
        {
            WithSettings(restriction, () =>
            {
                CoreAppSettings.Current.EnableStatusReporting = true;
                CoreAppSettings.Current.MaxRenewalRequests = 0;

                var hubManaged = GetClientPreferences();
                hubManaged.EnableStatusReporting = false;

                var localOnly = GetClientPreferences();
                localOnly.MaxRenewalRequests = 5;

                foreach (var prefs in new[] { hubManaged, localOnly })
                {
                    var result = SettingsManager.FromLocalPreferences(prefs);

                    Assert.IsFalse(result.IsSuccess);
                    Assert.AreEqual(LocalChangePolicy.GetRefusalMessage(restriction), result.Message);
                }

                Assert.IsTrue(CoreAppSettings.Current.EnableStatusReporting);
                Assert.AreEqual(0, CoreAppSettings.Current.MaxRenewalRequests);
            });
        }

        [TestMethod, Description("While restricted, maintenance windows and the UI language can still be changed locally")]
        [DataRow(LocalChangeRestriction.DeploymentTasksOnly)]
        [DataRow(LocalChangeRestriction.ReadOnly)]
        public void TestRestrictedPreferencesAllowMaintenanceWindowsAndLanguage(LocalChangeRestriction restriction)
        {
            WithSettings(restriction, () =>
            {
                CoreAppSettings.Current.Language = "en-US";
                CoreAppSettings.Current.MaintenanceWindows = [];

                var prefs = GetClientPreferences();
                prefs.Language = "fr-FR";
                prefs.MaintenanceWindows = [new MaintenanceWindow { Id = "window-1", Name = "Weekends" }];
                prefs.DefaultMaintenanceWindowId = "window-1";

                var result = SettingsManager.FromLocalPreferences(prefs);

                Assert.IsTrue(result.IsSuccess, result.Message);
                Assert.AreEqual("fr-FR", CoreAppSettings.Current.Language);
                Assert.AreEqual("window-1", CoreAppSettings.Current.MaintenanceWindows.Single().Id);
                Assert.AreEqual("window-1", CoreAppSettings.Current.DefaultMaintenanceWindowId);
                Assert.AreEqual(restriction, CoreAppSettings.Current.LocalChangeRestriction);
            });
        }

        [TestMethod, Description("While restricted, other settings sent with a maintenance window change are not kept, as the app may have loaded them before the hub changed them")]
        public void TestRestrictedPreferencesIgnoreStaleSettings()
        {
            WithSettings(LocalChangeRestriction.ReadOnly, () =>
            {
                CoreAppSettings.Current.EnableStatusReporting = true;
                CoreAppSettings.Current.MaintenanceWindows = [];

                var prefs = GetClientPreferences();
                prefs.EnableStatusReporting = false;
                prefs.MaintenanceWindows = [new MaintenanceWindow { Id = "window-1", Name = "Weekends" }];

                var result = SettingsManager.FromLocalPreferences(prefs);

                Assert.IsTrue(result.IsSuccess, result.Message);
                Assert.AreEqual("window-1", CoreAppSettings.Current.MaintenanceWindows.Single().Id);
                Assert.IsTrue(CoreAppSettings.Current.EnableStatusReporting);
            });
        }
    }
}
