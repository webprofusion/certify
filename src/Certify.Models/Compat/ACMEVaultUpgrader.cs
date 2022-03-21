using System;

namespace Certify.Models.Compat
{
    public class ACMEVaultUpgrader
    {
        private string _vaultPath = @"C:\\ProgramData\\ACMESharp\\sysVault\\00-VAULT";

        public bool HasACMEVault()
        {
            if (System.IO.File.Exists(_vaultPath))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1310:Specify StringComparison for correctness", Justification = "Legacy")]
        public string GetContact()
        {
            try
            {
                var vault = System.IO.File.ReadAllText(_vaultPath);
                var contactString = vault.Substring(vault.IndexOf("\"Contacts\""), vault.IndexOf("PublicKey") - vault.IndexOf("\"Contacts\""));
                contactString = contactString.Substring(contactString.IndexOf("["), contactString.IndexOf("]") - contactString.IndexOf("["));
                contactString = contactString.Substring(contactString.IndexOf(":") + 1, (contactString.LastIndexOf("\"") - contactString.IndexOf(":")) - 1).Trim();

                return contactString;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
