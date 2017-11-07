using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class RegistrationItem : VaultItem
    {
        public RegistrationItem() : base("registration") { }
        public IEnumerable<string> Contacts { get; set; }
    }

    public class IdentifierItem : VaultItem
    {
        public IdentifierItem() : base("identifier") { }
        public string Dns { get; set; }
        public string Status { get; set; }
        public DateTime? AuthorizationExpiry { get; set; }
        public bool IsAuthorizationPending { get; set; }
        public string ValidationError { get; set; }
        public string ValidationErrorType { get; set; }
    }

    public class CertificateItem : VaultItem
    {
        public CertificateItem() : base("certificate") { }
    }

    public class VaultItem
    {
        public string Id { get; set; }
        public string Alias { get; set; }
        public string Name { get; set; }
        public string ItemType { get; set; }

        public List<VaultItem> Children { get; set; }
        public VaultItem() { }
        public VaultItem(string itemType)
        {
            ItemType = itemType;
        }
    }
}