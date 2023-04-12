using System;
using System.Collections.Generic;

namespace Certify.Models
{
    [Obsolete("Legacy model, should not be referenced in new code")]
    public class RegistrationItem : VaultItem
    {
        public RegistrationItem() : base("registration")
        {
        }

        public IEnumerable<string> Contacts { get; set; } = new List<string>();
    }

    [Obsolete("Legacy model, should not be referenced in new code")]
    public class IdentifierItem : VaultItem
    {
        public IdentifierItem() : base("identifier")
        {
        }

        public string Dns { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? AuthorizationExpiry { get; set; }
        public bool IsAuthorizationPending { get; set; }
        public string ValidationError { get; set; } = string.Empty;
        public string ValidationErrorType { get; set; } = string.Empty;
    }

    [Obsolete("Legacy model, should not be referenced in new code")]
    public class CertificateItem : VaultItem
    {
        public CertificateItem() : base("certificate")
        {
        }
    }

    [Obsolete("Legacy model, should not be referenced in new code")]
    public class VaultItem
    {
        public string? Id { get; set; }
        public string? Alias { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;

        public VaultItem()
        {
        }

        public VaultItem(string itemType)
        {
            ItemType = itemType;
        }
    }
}
