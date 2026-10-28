using System;
using System.IO;
using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for loading and saving core app settings, in particular that the instance id survives a settings file
    /// which cannot be read, since the license file is encrypted with it
    /// </summary>
    [TestClass]
    public class SettingsManagerTests
    {
        private string _settingsPath = string.Empty;
        private CoreAppSettings _saved = null!;

        [TestInitialize]
        public void Setup()
        {
            var dir = Path.Combine(Path.GetTempPath(), "certify-settings-tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "appsettings.json");

            _saved = CoreAppSettings.Current;
        }

        [TestCleanup]
        public void Cleanup()
        {
            CoreAppSettings.Current = _saved;
            Directory.Delete(Path.GetDirectoryName(_settingsPath)!, recursive: true);
        }

        /// <summary>
        /// Settings as loaded from a file, with a known instance id
        /// </summary>
        private static CoreAppSettings CreateSettings(string instanceId)
        {
            var settings = JsonConvert.DeserializeObject<CoreAppSettings>("{}")!;
            settings.InstanceId = instanceId;
            settings.NotificationEmail = "admin@example.com";
            return settings;
        }

        [TestMethod, Description("Saved settings load back, and no temporary file is left behind")]
        public void TestSaveAndLoad()
        {
            CoreAppSettings.Current = CreateSettings("instance-a");
            SettingsManager.SaveAppSettings(_settingsPath);

            CoreAppSettings.Current = CreateSettings("other");
            SettingsManager.LoadAppSettings(_settingsPath);

            Assert.AreEqual("instance-a", CoreAppSettings.Current.InstanceId);
            Assert.AreEqual("admin@example.com", CoreAppSettings.Current.NotificationEmail);
            Assert.IsFalse(File.Exists(_settingsPath + ".tmp"));
        }

        [TestMethod, Description("A settings file which cannot be read leaves the settings in use, and the file, as they were")]
        [DataRow("")]
        [DataRow("{ \"InstanceId\": \"trunc")]
        public void TestUnreadableFileKeepsSettingsInUse(string fileContent)
        {
            File.WriteAllText(_settingsPath, fileContent);
            var inUse = CreateSettings("instance-a");
            CoreAppSettings.Current = inUse;

            SettingsManager.LoadAppSettings(_settingsPath);

            Assert.AreSame(inUse, CoreAppSettings.Current);
            Assert.AreEqual("instance-a", CoreAppSettings.Current.InstanceId);
            Assert.AreEqual("admin@example.com", CoreAppSettings.Current.NotificationEmail);
            Assert.AreEqual(fileContent, File.ReadAllText(_settingsPath));
        }

        [TestMethod, Description("With no settings in use, defaults stand in for an unreadable file but are never saved over it")]
        [DataRow("")]
        [DataRow("{ \"InstanceId\": \"trunc")]
        public void TestUnreadableFileFallbackDefaultsAreNotSaved(string fileContent)
        {
            File.WriteAllText(_settingsPath, fileContent);
            CoreAppSettings.Current = JsonConvert.DeserializeObject<CoreAppSettings>("{}")!;

            SettingsManager.LoadAppSettings(_settingsPath);

            Assert.IsFalse(string.IsNullOrEmpty(CoreAppSettings.Current.InstanceId), "Fallback defaults should still have an instance id");

            var fallbackInstanceId = CoreAppSettings.Current.InstanceId;

            Assert.ThrowsExactly<InvalidOperationException>(() => SettingsManager.SaveAppSettings(_settingsPath));
            Assert.AreEqual(fileContent, File.ReadAllText(_settingsPath));

            // a later failed load keeps the same fallback instance id rather than generating another
            SettingsManager.LoadAppSettings(_settingsPath);
            Assert.AreEqual(fallbackInstanceId, CoreAppSettings.Current.InstanceId);
        }

        [TestMethod, Description("Once the settings file can be read again, its settings replace the fallback defaults")]
        public void TestReadableFileReplacesFallbackDefaults()
        {
            File.WriteAllText(_settingsPath, "");
            CoreAppSettings.Current = JsonConvert.DeserializeObject<CoreAppSettings>("{}")!;
            SettingsManager.LoadAppSettings(_settingsPath);

            File.WriteAllText(_settingsPath, JsonConvert.SerializeObject(CreateSettings("instance-a")));
            SettingsManager.LoadAppSettings(_settingsPath);

            Assert.AreEqual("instance-a", CoreAppSettings.Current.InstanceId);

            // and can be saved again
            SettingsManager.SaveAppSettings(_settingsPath);
        }

        [TestMethod, Description("If the settings file is removed, the settings in use are written back rather than replaced with defaults")]
        public void TestMissingFileWritesBackSettingsInUse()
        {
            CoreAppSettings.Current = CreateSettings("instance-a");

            SettingsManager.LoadAppSettings(_settingsPath);

            Assert.AreEqual("instance-a", CoreAppSettings.Current.InstanceId);

            var written = JsonConvert.DeserializeObject<CoreAppSettings>(File.ReadAllText(_settingsPath))!;
            Assert.AreEqual("instance-a", written.InstanceId);
            Assert.AreEqual("admin@example.com", written.NotificationEmail);
        }

        [TestMethod, Description("A new install with no settings file gets defaults and a new instance id, saved")]
        public void TestNewInstallCreatesSettings()
        {
            CoreAppSettings.Current = JsonConvert.DeserializeObject<CoreAppSettings>("{}")!;

            SettingsManager.LoadAppSettings(_settingsPath);

            var written = JsonConvert.DeserializeObject<CoreAppSettings>(File.ReadAllText(_settingsPath))!;
            Assert.IsFalse(string.IsNullOrEmpty(written.InstanceId));
            Assert.AreEqual(CoreAppSettings.Current.InstanceId, written.InstanceId);
        }
    }
}
