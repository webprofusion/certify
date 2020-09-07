namespace Certify.Models
{
    public class StandardAuthTypes
    {
        /// <summary>
        /// An ACME Account with a certificate authority
        /// </summary>
        public const string STANDARD_ACME_ACCOUNT = "Certify.StandardChallenges.ACME";
        /// <summary>
        /// Run as the local user/service
        /// </summary>
        public const string STANDARD_AUTH_LOCAL = "Certify.StandardChallenges.Local";
        /// <summary>
        /// Run as a specific local user (windows)
        /// </summary>
        public const string STANDARD_AUTH_LOCAL_AS_USER = "Certify.StandardChallenges.LocalAsUser";
        /// <summary>
        /// Run using SSH credentials
        /// </summary>
        public const string STANDARD_AUTH_SSH = "Certify.StandardChallenges.SSH";
        /// <summary>
        /// Run using Windows network credentials 
        /// </summary>
        public const string STANDARD_AUTH_WINDOWS = "Certify.StandardChallenges.Windows";
        public const string STANDARD_AUTH_GENERIC = "Certify.StandardChallenges.Generic";
        public const string STANDARD_AUTH_PASSWORD = "Certify.StandardChallenges.Password";
        public const string STANDARD_AUTH_API_TOKEN = "Certify.StandardChallenges.ApiToken";
        public const string STANDARD_AUTH_PROVIDER_SPECIFIED = "Certify.StandardChallenges.ProviderSpecified";
    }
}
