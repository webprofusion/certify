using System.Collections.Generic;

namespace Certify.Models
{
    public class ExtendedVaultConfig
    {
        public List<VaultConfigItem> ConfigItems { get; set; }
    }

    public class VaultConfigItem
    {
        public string ItemType { get; set; }
        public string ItemValue { get; set; }
    }
}