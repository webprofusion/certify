namespace Certify.Models
{
    public class SupportedChallengeTypes
    {
        public const string CHALLENGE_TYPE_HTTP = "http-01";
        public const string CHALLENGE_TYPE_SNI = "tls-sni-01";
        public const string CHALLENGE_TYPE_DNS = "dns-01";
    }
}