namespace Certify.Models
{
    /// <summary>
    /// Supported CSR signing key algorithms 
    /// </summary>
    public static class SupportedCSRKeyAlgs
    {
        public static string RS256 = "RS256";

        public static string ECDSA256 = "ECDA256";

        public static string ECDSA384 = "ECDSA384";

        public static string ECDSA521 = "ECDSA521";
    }
}
