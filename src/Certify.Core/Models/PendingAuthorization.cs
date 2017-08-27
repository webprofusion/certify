using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify;

namespace Certify.Models
{
    public class AuthorizeChallengeItem
    {
        public string Status { get; set; }
        public object ChallengeData { get; set; }
    }

    public class PendingAuthorization
    {
        public AuthorizeChallengeItem Challenge { get; set; }
        public IdentifierItem Identifier { get; set; }
        public string TempFilePath { get; set; }
        public bool ExtensionlessConfigCheckedOK { get; set; }
    }
}