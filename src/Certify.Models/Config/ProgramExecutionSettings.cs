using System;
using System.Collections.Generic;
using System.Linq;
using Certify.Config;
using Certify.Models.Config.Migration;

namespace Certify.Models
{
    /// <summary>
    /// Settings which make an instance run a program or script named in the configuration: the PowerShell and program
    /// ("Run...") deployment tasks, the legacy pre/post request script fields, and the custom script DNS provider.
    ///
    /// Whoever sets these chooses code the instance runs as its service account, so adding or changing them is limited to
    /// administrators. Settings an item already has, saved again unchanged, do not count, so other edits to that item
    /// remain possible for anyone who may edit it.
    /// </summary>
    public static class ProgramExecutionSettings
    {
        /// <summary>
        /// The "Run..." deployment task, which starts a program with arguments
        /// </summary>
        public const string ProgramTaskTypeId = "Certify.Providers.DeploymentTasks.ShellExecute";

        /// <summary>
        /// The "(Use Custom Script)" DNS provider
        /// </summary>
        public const string ScriptDnsProviderId = "DNS01.Scripting";

        private static readonly HashSet<string> _programTaskTypeIds = new(StringComparer.OrdinalIgnoreCase)
        {
            StandardTaskTypes.POWERSHELL,
            ProgramTaskTypeId
        };

        /// <summary>
        /// True when the submitted managed item has a program execution setting the existing item does not have as it
        /// stands. A new item has no existing settings, so any it carries count.
        /// </summary>
        public static bool AddsOrChanges(ManagedCertificate? existing, ManagedCertificate? submitted)
            => Describe(submitted).Except(Describe(existing), StringComparer.Ordinal).Any();

        /// <summary>
        /// True when the submitted managed challenge configuration uses the script DNS provider in a way the existing
        /// configuration does not.
        /// </summary>
        public static bool AddsOrChanges(CertRequestChallengeConfig? existing, CertRequestChallengeConfig? submitted)
            => DescribeChallenges([submitted]).Except(DescribeChallenges([existing]), StringComparer.Ordinal).Any();

        /// <summary>
        /// True when an import package would store program execution settings: an item carrying them, or a bundled script.
        /// </summary>
        public static bool IsCarriedBy(ImportExportPackage? package)
            => package?.Content?.Scripts?.Count > 0
                || package?.Content?.ManagedCertificates?.Any(item => Describe(item).Any()) == true;

        /// <summary>
        /// Each program execution setting on an item, described by everything which decides what runs and how: its type,
        /// parameters, execution context, credential, target and trigger. Names, descriptions and run history are left
        /// out, as they change nothing about what is run.
        /// </summary>
        private static IEnumerable<string> Describe(ManagedCertificate? item)
        {
            if (item == null)
            {
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(item.RequestConfig?.PreRequestPowerShellScript))
            {
                yield return $"prerequestscript|{item.RequestConfig!.PreRequestPowerShellScript}";
            }

            if (!string.IsNullOrWhiteSpace(item.RequestConfig?.PostRequestPowerShellScript))
            {
                yield return $"postrequestscript|{item.RequestConfig!.PostRequestPowerShellScript}";
            }

            var tasks = (item.PreRequestTasks ?? []).Select(t => ("pre", t))
                .Concat((item.PostRequestTasks ?? []).Select(t => ("post", t)));

            foreach (var (stage, task) in tasks)
            {
                if (task?.TaskTypeId != null && _programTaskTypeIds.Contains(task.TaskTypeId))
                {
                    yield return string.Join("|",
                        "task",
                        stage,
                        task.TaskTypeId.ToLowerInvariant(),
                        task.ChallengeProvider,
                        task.ChallengeCredentialKey,
                        task.TargetHost,
                        task.TaskTrigger,
                        task.RunIfLastStepFailed,
                        DescribeParameters(task.Parameters?.Select(p => (p.Key, p.Value))));
                }
            }

            foreach (var challenge in DescribeChallenges(item.RequestConfig?.Challenges ?? []))
            {
                yield return challenge;
            }
        }

        private static IEnumerable<string> DescribeChallenges(IEnumerable<CertRequestChallengeConfig?> challenges)
            => challenges
                .Where(c => string.Equals(c?.ChallengeProvider, ScriptDnsProviderId, StringComparison.OrdinalIgnoreCase))
                .Select(c => string.Join("|",
                    "challenge",
                    ScriptDnsProviderId.ToLowerInvariant(),
                    c!.ChallengeCredentialKey,
                    DescribeParameters(c.Parameters?.Select(p => (p.Key, p.Value)))));

        private static string DescribeParameters(IEnumerable<(string? Key, string? Value)>? parameters)
            => string.Join(";", (parameters ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.Key))
                .Select(p => $"{p.Key!.ToLowerInvariant()}={p.Value}")
                .OrderBy(p => p, StringComparer.Ordinal));
    }
}
