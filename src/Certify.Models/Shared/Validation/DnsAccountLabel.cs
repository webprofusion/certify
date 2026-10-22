using System;
using System.Security.Cryptography;
using System.Text;

namespace Certify.Models.Shared.Validation
{
    /// <summary>
    /// Record names for the dns-account-01 challenge (draft-ietf-acme-dns-account-label). The TXT value is the same as
    /// for dns-01, but the record is at an account specific label, _{label}._acme-challenge.{domain}, so several ACME
    /// accounts (or clients) can validate the same domain, each with their own record or CNAME delegation.
    /// </summary>
    public static class DnsAccountLabel
    {
        public const string SpecificationUrl = "https://datatracker.ietf.org/doc/draft-ietf-acme-dns-account-label/";

        private const string ChallengeLabel = "._acme-challenge.";
        private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        /// <summary>
        /// The account label: "_" + base32(SHA-256(account URL)[0:10]), in lower case. Ten bytes encode to exactly
        /// 16 base32 characters, with no padding.
        /// </summary>
        public static string GetAccountLabel(string accountUrl)
        {
            if (string.IsNullOrWhiteSpace(accountUrl))
            {
                throw new ArgumentException("The ACME account URL is required for a dns-account-01 record name.", nameof(accountUrl));
            }

            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(accountUrl));
            }

            var label = new StringBuilder("_", 17);
            var buffer = 0;
            var bits = 0;

            for (var i = 0; i < 10; i++)
            {
                buffer = (buffer << 8) | hash[i];
                bits += 8;

                while (bits >= 5)
                {
                    label.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }

            return label.ToString();
        }

        /// <summary>
        /// The TXT record name for an identifier: _{label}._acme-challenge.{domain}. A wildcard is validated at its base domain.
        /// </summary>
        public static string GetRecordName(string accountUrl, string identifier)
        {
            var domain = (identifier ?? string.Empty).Trim();
            if (domain.StartsWith("*.", StringComparison.Ordinal))
            {
                domain = domain.Substring(2);
            }

            return $"{GetAccountLabel(accountUrl)}{ChallengeLabel}{domain}";
        }

        /// <summary>
        /// True when a record name is a dns-account-01 record for the domain: an account label (an underscore and 16
        /// base32 characters, in either case) then _acme-challenge then the domain. Both names should already be normalised.
        /// </summary>
        public static bool IsAccountRecordName(string? recordName, string? domain)
        {
            if (string.IsNullOrEmpty(domain) || GetAccountLabelFromRecordName(recordName) == null)
            {
                return false;
            }

            var suffix = ChallengeLabel + domain;
            return recordName!.Length == 17 + suffix.Length && recordName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The account label a record name starts with, in lower case, when it is a dns-account-01 name for any domain
        /// (_{label}._acme-challenge.{domain}, where the domain may be a CNAME delegation target), otherwise null
        /// </summary>
        public static string? GetAccountLabelFromRecordName(string? recordName)
        {
            var name = recordName?.Trim();

            if (name == null || name.Length <= 17 + ChallengeLabel.Length || name[0] != '_'
                || string.Compare(name, 17, ChallengeLabel, 0, ChallengeLabel.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return null;
            }

            for (var i = 1; i < 17; i++)
            {
                if (Base32Alphabet.IndexOf(char.ToLowerInvariant(name[i])) < 0)
                {
                    return null;
                }
            }

            return name.Substring(0, 17).ToLowerInvariant();
        }
    }
}
