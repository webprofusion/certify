namespace Certify.Models.Config
{
    public class ChallengeCredential
    {
        /// <summary>
        /// Which stored credential do we use to perform this challenge (DNS api key etc) 
        /// </summary>
        public string CredentialStorageKey { get; set; }

        public string ChallengeProvider { get; set; }
    }
}