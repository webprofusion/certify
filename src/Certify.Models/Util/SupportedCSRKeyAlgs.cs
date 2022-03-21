namespace Certify.Models
{
    /// <summary>
    /// Supported CSR signing key algorithms 
    /// </summary>
    public static class SupportedCSRKeyAlgs
    {
        public const string RS256 = "RS256";

        public const string ECDSA256 = "ECDA256";

        public const string ECDSA384 = "ECDSA384";

        public const string ECDSA521 = "ECDSA521";
    }
}
