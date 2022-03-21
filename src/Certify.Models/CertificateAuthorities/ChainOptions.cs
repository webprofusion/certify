using System.Collections.Generic;

namespace Certify.CertificateAuthorities
{
    public class ChainActions
    {
        public const string Delete = "Delete";
        public const string StoreCARoot = "StoreCARoot";
        public const string StoreCAIntermediate = "StoreCAIntermediate";
    }
    public class ChainAction
    {
        /// <summary>
        /// e.g. Delete, StoreCARoot, StoreCAIntermediate
        /// </summary>
        public string Action { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// Reference for certificate
        /// </summary>
        public string CertificateThumbprint { get; set; }

        public ChainAction(string action, string thumbprint, string description)
        {
            Action = action;
            Description = description;
            CertificateThumbprint = thumbprint;
        }
    }

    public class ChainOption
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Preferred Issuer required for this chain end-to-end
        /// </summary>
        public string Issuer { get; set; } = string.Empty;

        /// <summary>
        /// RSA, ECDSA
        /// </summary>
        public string ChainGroup { get; set; } = string.Empty;

        /// <summary>
        /// List of trust store actions required to select this chain
        /// </summary>
        public List<ChainAction> Actions { get; set; } = new();
    }

}
