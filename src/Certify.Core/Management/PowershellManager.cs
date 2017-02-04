using ACMESharp.Vault.Model;
using ACMESharp.Vault.Providers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Tasks;
using ACMESharp;
using Certify.Models;

namespace Certify
{
    public class PowershellManager
    {
        private PowerShell ps = null;
        private List<ActionLogItem> ActionLogs = null;

        private readonly IdnMapping _idnMapping = new IdnMapping();

        public PowershellManager(string workingDirectory, List<ActionLogItem> actionLogs)
        {
            this.ActionLogs = actionLogs;

            InitialSessionState initial = InitialSessionState.CreateDefault();
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            initial.ImportPSModule(new string[] { appDirectory + "ACMESharp.psd1", appDirectory + "ACMESharp-Providers\\ACMESharp-Providers.psd1" });
            Runspace runspace = null;
            try
            {
                //attempt to start PowerShell with ACMESharp module loaded, if execution policy is restricted this will fail
                runspace = RunspaceFactory.CreateRunspace(initial);
                runspace.Open();
                ps = PowerShell.Create();
                ps.Runspace = runspace;
            }
            catch (CmdletInvocationException exp)
            {
                //elevate execution policy and attempt to load ACMESharp Module again
                //allow remote signed scripts to run (required for module loading)
                runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault());
                runspace.Open();
                ps = PowerShell.Create();
                ps.Runspace = runspace;

                var cmd = ps.AddCommand("Set-ExecutionPolicy");
                cmd.AddParameter("ExecutionPolicy", "RemoteSigned");
                cmd.AddParameter("Force");
                cmd.AddParameter("Scope", "Process");

                var res = InvokeCurrentPSCommand();

                ps.Commands.Clear();
                ps.AddCommand("Import-Module").AddArgument(appDirectory + "ACMESharp");
                ps.Invoke();
            }

            /*if (!IsAcmeSharpModuleInstalled())
            {
                System.Diagnostics.Debug.WriteLine("No ACMESharp Available");
            }*/
            if (System.IO.Directory.Exists(workingDirectory))
            {
                SetWorkingDirectory(workingDirectory);
            }
        }

        public int GetPowershellVersion()
        {
            int powershellVersion = 0;
            string regval = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\PowerShell\3\PowerShellEngine", "PowerShellVersion", null).ToString();
            if (regval != null)
            {
                string[] ver = regval.Split('.');
                powershellVersion = int.Parse(ver[0]);
            }
            return powershellVersion;
        }

        public bool IsValidVersion()
        {
            var version = GetPowershellVersion();

            //If PS Version 1, 2 or doesn't exist then return false.
            return !version.Equals(1) && !version.Equals(2) && !version.Equals(0);
        }

        public bool IsAcmeSharpModuleInstalled()
        {
            ps.Commands.Clear();
            var cmd = ps.Commands.AddCommand("Get-Module");
            cmd.AddParameter("ListAvailable");
            cmd.AddArgument("ACMESharp");

            LogAction("Powershell: Get-Module -ListAvailable ACMESharp");

            var res = InvokeCurrentPSCommand();

            return res.IsOK && (res.Result as
                    System.Collections.IEnumerable)?.OfType<object>().Count() > 0;
        }

        private void LogAction(string command, string result = null)
        {
            if (this.ActionLogs != null)
            {
                this.ActionLogs.Add(new ActionLogItem { Command = command, Result = result, DateTime = DateTime.Now });
            }
        }

        public void SetWorkingDirectory(string path)
        {
            LogAction("Powershell: Setting Working Directory to " + path);
            ps.Runspace.SessionStateProxy.Path.SetLocation(path);
        }

        #region API

        private APIResult InvokeCurrentPSCommand()
        {
            try
            {
                var results = ps.Invoke();
                return new APIResult { IsOK = true, Result = results };
            }
            catch (AcmeClient.AcmeWebException awExp)
            {
                if (awExp.Response != null && awExp.Response.ProblemDetail != null)
                {
                    LogAction("[ACME Error]: " + awExp.Response.ProblemDetail.Detail);
                }

                return new APIResult { IsOK = false, Message = awExp.ToString(), Result = awExp };
            }
            catch (Exception exp)
            {
                LogAction("[Error]: " + exp.ToString());

                return new APIResult { IsOK = false, Message = exp.ToString(), Result = exp };
            }
        }

        public APIResult InitializeVault(string baseURI)
        {
            //Initialize-ACMEVault -BaseURI   https://acme-v01.api.letsencrypt.org/
            ps.Commands.Clear();
            var cmd = ps.Commands.AddCommand("Initialize-ACMEVault");
            cmd.AddParameter("BaseURI", baseURI);

            LogAction("Powershell: Initialize-ACMEVault -BaseURI " + baseURI);

            return InvokeCurrentPSCommand();
        }

        public APIResult NewRegistration(string contacts)
        {
            ps.Commands.Clear();
            var cmd = ps.Commands.AddCommand("New-ACMERegistration");
            cmd.AddParameter("Contacts", contacts);

            LogAction("Powershell: New-ACMERegistration -Contacts " + contacts);

            return InvokeCurrentPSCommand();
        }

        public APIResult AcceptRegistrationTOS()
        {
            ps.Commands.Clear();
            var cmd = ps.Commands.AddCommand("Update-ACMERegistration");
            cmd.AddParameter("AcceptTOS");

            LogAction("Powershell: Update-ACMERegistration -AcceptTOS");

            return InvokeCurrentPSCommand();
        }

        public APIResult NewProviderConfig(string providerType, string alias)
        {
            ps.Commands.Clear();
            var cmd = ps.Commands.AddCommand("New-ACMEProviderConfig");
            cmd.AddParameter("WebServerProvider", providerType);
            cmd.AddParameter("Alias", alias);
            cmd.AddParameter("SkipEdit", true);

            LogAction("Powershell: New-ACMEProviderConfig -WebServerProvider " + providerType + " -Alias " + alias + " -SkipEdit true");
            return InvokeCurrentPSCommand();
        }

        public APIResult NewIdentifier(string dns, string alias, string label)
        {
            ps.Commands.Clear();

            // ACME service requires international domain names in ascii mode
            dns = _idnMapping.GetAscii(dns);

            var cmd = ps.Commands.AddCommand("New-ACMEIdentifier");
            cmd.AddParameter("Dns", dns);
            cmd.AddParameter("Alias", alias);
            cmd.AddParameter("Label", label);

            LogAction("Powershell: New-ACMEIdentifier -Dns " + dns + " -Alias " + alias + " -Label " + label);
            return InvokeCurrentPSCommand();
        }

        public APIResult UpdateIdentifier(string alias, string challenge = null)
        {
            ps.Commands.Clear();

            var cmd = ps.Commands.AddCommand("Update-ACMEIdentifier");
            cmd.AddParameter("Ref", alias);
            if (challenge != null)
            {
                cmd.AddParameter("Challenge", challenge);
            }

            LogAction("Powershell: Update-ACMEIdentifier -Ref " + alias);

            return InvokeCurrentPSCommand();
        }

        public CertificateInfo GetCertificateByRef(string certRef)
        {
            ps.Commands.Clear();
            var cmd = ps.Commands.AddCommand("Get-ACMECertificate");
            cmd.AddParameter("Ref", certRef);
            cmd.AddParameter("Overwrite");

            LogAction("Powershell: Get-ACMECertificate -Ref " + certRef + " -Overwrite");

            var result = InvokeCurrentPSCommand();

            if (result.IsOK)
            {
                var psResult = (Collection<PSObject>)result.Result;
                if (psResult.Any(r => r.BaseObject is CertificateInfo))
                {
                    var cert = (CertificateInfo)psResult.FirstOrDefault(r => r.BaseObject is CertificateInfo).BaseObject;
                    return cert;
                }
            }

            return null;
        }

        public APIResult CompleteChallenge(string identifierRef, string challengeType = "http-01", bool regenerate = true)
        {
            ps.Commands.Clear();

            var cmd = ps.Commands.AddCommand("Complete-ACMEChallenge");
            cmd.AddParameter("Ref", identifierRef);
            cmd.AddParameter("ChallengeType", challengeType);
            cmd.AddParameter("Handler", "manual");

            cmd.AddParameter("Repeat");

            if (regenerate)
            {
                cmd.AddParameter("Regenerate");
            }

            LogAction("Powershell: Complete-ACMEChallenge -Ref " + identifierRef + " -ChallengeType " + challengeType + " -Handler manual " + (regenerate ? " -Regenerate" : ""));

            return InvokeCurrentPSCommand();
        }

        public APIResult SubmitChallenge(string identifierRef, string challengeType = "http-01")
        {
            //TODO: if challenge already exists, check status, may have certs waiting already
            string logAction = "Powershell: Submit-ACMEChallenge " + identifierRef + " -Challenge " + challengeType;

            ps.Commands.Clear();

            var cmd = ps.Commands.AddCommand("Submit-ACMEChallenge");
            cmd.AddParameter("Ref", identifierRef);
            cmd.AddParameter("Challenge", challengeType);

            LogAction("Powershell: Submit-ACMEChallenge -Ref " + identifierRef + " -Challenge " + challengeType);

            return InvokeCurrentPSCommand();
        }

        public APIResult NewCertificate(string identifierRef, string certAlias, string[] subjectAlternativeNameIdentifiers = null)
        {
            ps.Commands.Clear();

            var cmd = ps.Commands.AddCommand("New-ACMECertificate");
            cmd.AddParameter("Identifier", identifierRef);
            cmd.AddParameter("Alias", certAlias);

            string sanList = null;
            if (subjectAlternativeNameIdentifiers != null && subjectAlternativeNameIdentifiers.Length > 0)
            {
                sanList = string.Join(",", subjectAlternativeNameIdentifiers);
                cmd.AddParameter("AlternativeIdentifierRefs", sanList);
            }
            cmd.AddParameter("Generate");

            LogAction("Powershell: New-ACMECertificate -Identifier " + identifierRef + " -Alias " + certAlias + " -Generate" + (sanList != null ? " -AlternativeIdentifierRefs " + sanList : ""));

            return InvokeCurrentPSCommand();
        }

        public APIResult SubmitCertificate(string certAlias)
        {
            ps.Commands.Clear();

            var cmd = ps.Commands.AddCommand("Submit-ACMECertificate");
            cmd.AddParameter("Ref", certAlias);
            //  cmd.AddParameter("Force", force);
            var results = ps.Invoke();

            LogAction("Powershell: Submit-ACMECertificate -Ref " + certAlias);

            return InvokeCurrentPSCommand();
        }

        public APIResult UpdateCertificate(string certAlias)
        {
            ps.Commands.Clear();

            var cmd = ps.Commands.AddCommand("Update-ACMECertificate");
            cmd.AddParameter("Ref", certAlias);

            var results = ps.Invoke();

            LogAction("Powershell: Update-ACMECertificate -Ref " + certAlias);

            return InvokeCurrentPSCommand();
        }

        public APIResult ExportCertificate(string certAlias, string vaultFolderPath, bool pfxOnly = false)
        {
            string certKey = certAlias;
            if (certKey.StartsWith("=")) certKey = certKey.Replace("=", "");
            ps.Commands.Clear();

            var cmd = ps.Commands.AddCommand("Get-ACMECertificate");
            cmd.AddParameter("Ref", certAlias);
            if (!pfxOnly)
            {
                cmd.AddParameter("ExportKeyPEM", vaultFolderPath + "\\" + LocalDiskVault.KEYPM + "\\" + certKey + "-key.pem");
                cmd.AddParameter("ExportCsrPEM", vaultFolderPath + "\\" + LocalDiskVault.CSRPM + "\\" + certKey + "-csr.pem");
                cmd.AddParameter("ExportCertificatePEM", vaultFolderPath + "\\" + LocalDiskVault.CRTPM + "\\" + certKey + "-crt.pem");
                cmd.AddParameter("ExportCertificateDER", vaultFolderPath + "\\" + LocalDiskVault.CRTDR + "\\" + certKey + "-crt.der");
            }
            cmd.AddParameter("ExportPkcs12", vaultFolderPath + "\\" + LocalDiskVault.ASSET + "\\" + certKey + "-all.pfx");
            cmd.AddParameter("Overwrite");

            LogAction("Powershell: Get-ACMECertificate -Ref " + certAlias
                + (!pfxOnly ?
                " -ExportKeyPEM " + vaultFolderPath + "\\" + LocalDiskVault.KEYPM + "\\" + certKey + "-key.pem"
                + " -ExportCsrPEM " + vaultFolderPath + "\\" + LocalDiskVault.CSRPM + "\\" + certKey + "-csr.pem"
                + " -ExportCertificatePEM " + vaultFolderPath + "\\" + LocalDiskVault.CRTPM + "\\" + certKey + "-crt.pem"
                + " -ExportCertificateDER " + vaultFolderPath + "\\" + LocalDiskVault.CRTDR + "\\" + certKey + "-csr.der"
                : "")
                + " -ExportPkcs12 " + vaultFolderPath + "\\" + LocalDiskVault.ASSET + "\\" + certKey + "-all.pfx"
                + " -Overwrite"
                );

            return InvokeCurrentPSCommand();
        }

        #endregion API
    }
}