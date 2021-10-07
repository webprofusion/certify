using System;
using System.Collections.Generic;
using System.Text;

namespace Certify.CertificateAuthorities
{
    public class ChainActions
    {
        public static string Delete = "Delete";
        public static string StoreCARoot = "StoreCARoot";
        public static string StoreCAIntermediate = "StoreCAIntermediate";
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
    }

    public class ChainOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// Preferred Issuer required for this chain end-to-end
        /// </summary>
        public string Issuer { get; set; }

        /// <summary>
        /// RSA, ECDSA
        /// </summary>
        public string ChainGroup { get; set; } 

        /// <summary>
        /// List of trust store actions required to select this chain
        /// </summary>
        public List<ChainAction> Actions { get; set; }
    }

}
