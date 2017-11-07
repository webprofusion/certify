using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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