namespace Certify.Models.Hub
{
    /// <summary>
    /// A Managed ACME directory key lets an ACME client without External Account Binding support register with the
    /// hub, by using a directory url which carries the key: {hub}/acme/{key}/directory.
    /// The key is the access token id and a hash of the token secret. The hash input differs from the EAB key (a hash
    /// of the secret alone), so neither can be derived from the other.
    /// </summary>
    public static class ManagedAcmeDirectoryKey
    {
        public const char Separator = '.';

        /// <summary>
        /// The value hashed (SHA256, base64url) to give the secret part of the key
        /// </summary>
        public static string GetHashInput(string secret) => $"certify-acme-directory-key:{secret}";

        public static string Build(string tokenId, string secretHash) => $"{tokenId}{Separator}{secretHash}";

        public static bool TryParse(string? key, out string tokenId, out string secretHash)
        {
            tokenId = string.Empty;
            secretHash = string.Empty;

            // base64url has no '.', so the hash follows the last separator
            var index = key?.LastIndexOf(Separator) ?? -1;

            if (key == null || index <= 0 || index == key.Length - 1)
            {
                return false;
            }

            tokenId = key.Substring(0, index);
            secretHash = key.Substring(index + 1);

            return true;
        }
    }
}
