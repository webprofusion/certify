namespace Certify.Models
{
    public class SupportedChallengeTypes
    {
        public const string CHALLENGE_TYPE_HTTP = "http-01";
        public const string CHALLENGE_TYPE_SNI = "tls-sni-01";
        public const string CHALLENGE_TYPE_DNS = "dns-01";
        public const string CHALLENGE_TYPE_DNS_ACCOUNT = "dns-account-01";
        public const string CHALLENGE_TYPE_DNS_PERSIST = "dns-persist-01";
        public const string CHALLENGE_TYPE_TKAUTH = "tkauth-01";

        /// <summary>
        /// True for the challenge types answered by creating a TXT record through a DNS provider: dns-01, and
        /// dns-account-01 which differs only in the record name. DNS providers are defined for dns-01 and serve both.
        /// </summary>
        public static bool UsesDnsProvider(string? challengeType)
        {
            return challengeType == CHALLENGE_TYPE_DNS || challengeType == CHALLENGE_TYPE_DNS_ACCOUNT;
        }

        /// <summary>
        /// The challenge type DNS providers are defined for, when listing the providers usable with a challenge type
        /// </summary>
        public static string? GetProviderChallengeType(string? challengeType)
        {
            return UsesDnsProvider(challengeType) ? CHALLENGE_TYPE_DNS : challengeType;
        }
    }
}
