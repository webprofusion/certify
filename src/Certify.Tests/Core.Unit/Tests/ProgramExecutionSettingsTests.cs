using System.Collections.Generic;
using System.Collections.ObjectModel;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Config.Migration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Deciding whether a submitted configuration adds or changes a setting which runs a program or script, which only an
    /// administrator may do. Settings an item already has, saved again unchanged, do not count.
    /// </summary>
    [TestClass]
    public class ProgramExecutionSettingsTests
    {
        [TestMethod]
        public void AddsOrChanges_ItemWithoutProgramSettings_IsFalse()
        {
            var item = Item(Task(StandardTaskTypes.WEBHOOK, ("url", "https://example.com/hook")));

            Assert.IsFalse(ProgramExecutionSettings.AddsOrChanges(existing: null, item));
        }

        [TestMethod]
        [DataRow(ProgramExecutionSettings.ProgramTaskTypeId)]
        [DataRow(StandardTaskTypes.POWERSHELL)]
        public void AddsOrChanges_NewItemWithProgramTask_IsTrue(string taskTypeId)
        {
            Assert.IsTrue(ProgramExecutionSettings.AddsOrChanges(existing: null, Item(Task(taskTypeId, ("path", "run.cmd")))));
        }

        [TestMethod]
        public void AddsOrChanges_ProgramTaskSavedUnchanged_IsFalse()
        {
            var existing = Item(Task(ProgramExecutionSettings.ProgramTaskTypeId, ("path", "run.cmd"), ("args", "a=1")));

            var submitted = Item(Task(ProgramExecutionSettings.ProgramTaskTypeId, ("args", "a=1"), ("path", "run.cmd")));
            submitted.PostRequestTasks![0].Id = "renamed-id";
            submitted.PostRequestTasks[0].TaskName = "Renamed";
            submitted.PostRequestTasks[0].LastResult = "last run result";

            Assert.IsFalse(ProgramExecutionSettings.AddsOrChanges(existing, submitted), "names, ids, run history and parameter order change nothing that runs");
        }

        [TestMethod]
        public void AddsOrChanges_ProgramTaskParameterChanged_IsTrue()
        {
            var existing = Item(Task(ProgramExecutionSettings.ProgramTaskTypeId, ("path", "run.cmd")));
            var submitted = Item(Task(ProgramExecutionSettings.ProgramTaskTypeId, ("path", "other.cmd")));

            Assert.IsTrue(ProgramExecutionSettings.AddsOrChanges(existing, submitted));
        }

        [TestMethod]
        public void AddsOrChanges_ProgramTaskTriggerOrContextChanged_IsTrue()
        {
            var existing = Item(Task(StandardTaskTypes.POWERSHELL, ("scriptpath", "deploy.ps1")));

            var retriggered = Item(Task(StandardTaskTypes.POWERSHELL, ("scriptpath", "deploy.ps1")));
            retriggered.PostRequestTasks![0].TaskTrigger = TaskTriggerType.ON_ERROR;

            var recredentialed = Item(Task(StandardTaskTypes.POWERSHELL, ("scriptpath", "deploy.ps1")));
            recredentialed.PostRequestTasks![0].ChallengeCredentialKey = "other-credential";

            Assert.IsTrue(ProgramExecutionSettings.AddsOrChanges(existing, retriggered));
            Assert.IsTrue(ProgramExecutionSettings.AddsOrChanges(existing, recredentialed));
        }

        [TestMethod]
        public void AddsOrChanges_ProgramTaskRemoved_IsFalse()
        {
            var existing = Item(Task(ProgramExecutionSettings.ProgramTaskTypeId, ("path", "run.cmd")));

            Assert.IsFalse(ProgramExecutionSettings.AddsOrChanges(existing, Item()));
        }

        [TestMethod]
        public void AddsOrChanges_LegacyScriptField_IsTrue()
        {
            var submitted = Item();
            submitted.RequestConfig.PostRequestPowerShellScript = "deploy.ps1";

            Assert.IsTrue(ProgramExecutionSettings.AddsOrChanges(existing: null, submitted));
        }

        [TestMethod]
        public void AddsOrChanges_ScriptDnsProvider_IsTrueUntilSavedUnchanged()
        {
            var submitted = Item();
            submitted.RequestConfig.Challenges = [ScriptChallenge("create.cmd")];

            var existing = Item();
            existing.RequestConfig.Challenges = [ScriptChallenge("create.cmd")];

            Assert.IsTrue(ProgramExecutionSettings.AddsOrChanges(existing: null, submitted));
            Assert.IsFalse(ProgramExecutionSettings.AddsOrChanges(existing, submitted));
        }

        [TestMethod]
        public void AddsOrChanges_OtherDnsProvider_IsFalse()
        {
            var submitted = Item();
            submitted.RequestConfig.Challenges = [new CertRequestChallengeConfig { ChallengeType = "dns-01", ChallengeProvider = "DNS01.API.Cloudflare" }];

            Assert.IsFalse(ProgramExecutionSettings.AddsOrChanges(existing: null, submitted));
        }

        [TestMethod]
        public void AddsOrChanges_ManagedChallengeConfig()
        {
            Assert.IsTrue(ProgramExecutionSettings.AddsOrChanges(existing: null, ScriptChallenge("create.cmd")));
            Assert.IsTrue(ProgramExecutionSettings.AddsOrChanges(ScriptChallenge("create.cmd"), ScriptChallenge("other.cmd")));
            Assert.IsFalse(ProgramExecutionSettings.AddsOrChanges(ScriptChallenge("create.cmd"), ScriptChallenge("create.cmd")));
            Assert.IsFalse(ProgramExecutionSettings.AddsOrChanges(existing: null, new CertRequestChallengeConfig { ChallengeProvider = "DNS01.API.Cloudflare" }));
        }

        [TestMethod]
        public void IsCarriedBy_ImportPackage()
        {
            Assert.IsFalse(ProgramExecutionSettings.IsCarriedBy(Package(Item())));
            Assert.IsTrue(ProgramExecutionSettings.IsCarriedBy(Package(Item(Task(StandardTaskTypes.POWERSHELL, ("scriptpath", "deploy.ps1"))))));

            var withBundledScript = Package(Item());
            withBundledScript.Content!.Scripts = [new EncryptedContent { Filename = "deploy.ps1" }];

            Assert.IsTrue(ProgramExecutionSettings.IsCarriedBy(withBundledScript));
        }

        private static ManagedCertificate Item(params DeploymentTaskConfig[] postRequestTasks)
            => new()
            {
                Id = "item-1",
                RequestConfig = new CertRequestConfig(),
                PostRequestTasks = new ObservableCollection<DeploymentTaskConfig>(postRequestTasks)
            };

        private static DeploymentTaskConfig Task(string taskTypeId, params (string Key, string Value)[] parameters)
        {
            var settings = new List<ProviderParameterSetting>();

            foreach (var (key, value) in parameters)
            {
                settings.Add(new ProviderParameterSetting(key, value));
            }

            return new DeploymentTaskConfig
            {
                Id = "task-1",
                TaskTypeId = taskTypeId,
                TaskName = "Task",
                Parameters = settings
            };
        }

        private static CertRequestChallengeConfig ScriptChallenge(string createScript)
            => new()
            {
                ChallengeType = "dns-01",
                ChallengeProvider = ProgramExecutionSettings.ScriptDnsProviderId,
                Parameters = [new ProviderParameter { Key = "createscriptpath", Value = createScript }]
            };

        private static ImportExportPackage Package(params ManagedCertificate[] items)
            => new() { Content = new ImportExportContent { ManagedCertificates = [.. items] } };
    }
}
