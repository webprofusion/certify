using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify;
using ACMESharp;
using ACMESharp.Vault.Model;

namespace Certify.Models
{
    public class PendingAuthorization
    {
        public AuthorizeChallenge Challenge { get; set; }
        public IdentifierInfo Identifier { get; set; }
        public string TempFilePath { get; set; }
        public bool ExtensionlessConfigCheckedOK { get; set; }
    }
}
