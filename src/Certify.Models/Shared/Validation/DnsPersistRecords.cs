using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Certify.Models.Shared.Validation
{
    /// <summary>
    /// The accounturi form a CA requires, which depends on the revision of draft-ietf-acme-dns-persist it follows
    /// </summary>
    public enum DnsPersistVariant
    {
        /// <summary>
        /// The plain ACME account URL: draft-ietf-acme-dns-persist-00 and -01 (and draft-sheurich-acme-dns-persist).
        /// Draft-01 also gives it as accounturi in the challenge object, which must be the account URL.
        /// </summary>
        PlainAccountUrl = 0,

        /// <summary>
        /// draft-ietf-acme-dns-persist-02: a hashed account URI under the CA's accountHashPrefix
        /// </summary>
        HashedAccountUri = 2
    }

    /// <summary>
    /// What a CA publishes about dns-persist-01 in its ACME directory, for one ACME account
    /// </summary>
    public class DnsPersistDirectoryInfo
    {
        public string? AccountUri { get; set; }
        public string? CertificateAuthorityId { get; set; }
        public bool IsStagingAccount { get; set; }

        /// <summary>
        /// Unpadded base64url SHA-256 JWK thumbprint of the account's current key
        /// </summary>
        public string? AccountThumbprint { get; set; }

        /// <summary>
        /// False if the CA directory could not be fetched, in which case the other directory values are unknown
        /// </summary>
        public bool IsDirectoryAvailable { get; set; }

        /// <summary>
        /// meta.issuerDomainNames (draft-02)
        /// </summary>
        public List<string>? IssuerDomainNames { get; set; }

        /// <summary>
        /// meta.accountHashPrefix (draft-02)
        /// </summary>
        public string? AccountHashPrefix { get; set; }

        /// <summary>
        /// meta.caaIdentities, which earlier drafts allow the CA to draw its issuer domain names from
        /// </summary>
        public List<string>? CaaIdentities { get; set; }

        public DnsPersistVariant GetVariant() => DnsPersistRecordBuilder.DetectVariant(AccountHashPrefix);

        /// <summary>
        /// Issuer domain names a record can use before an order exists: the directory list (draft-02), otherwise the
        /// CAA identities, which earlier drafts only give as a hint
        /// </summary>
        public List<string> GetKnownIssuerDomainNames()
        {
            var names = IssuerDomainNames?.Any() == true ? IssuerDomainNames : CaaIdentities;
            return DnsPersistRecordBuilder.NormalizeDomainNames(names);
        }
    }

    /// <summary>
    /// The values a dns-persist-01 challenge (or a simulated one, for a configuration test) is checked against
    /// </summary>
    public class DnsPersistChallengeInfo
    {
        public DnsPersistVariant Variant { get; set; }

        /// <summary>
        /// The challenge's issuer domain names. Empty for a simulated challenge, where any known issuer is accepted.
        /// </summary>
        public List<string> IssuerDomainNames { get; set; } = new List<string>();

        /// <summary>
        /// The CA directory's issuer domain names (draft-02), or its CAA identities for earlier drafts
        /// </summary>
        public List<string> DirectoryIssuerDomainNames { get; set; } = new List<string>();

        public string? AccountHashPrefix { get; set; }

        public string? AccountUrl { get; set; }

        public string? AccountThumbprint { get; set; }

        /// <summary>
        /// The values for a simulated challenge, for a configuration test, where the challenge's issuer domain names are not known
        /// </summary>
        public static DnsPersistChallengeInfo FromDirectory(DnsPersistDirectoryInfo directory)
        {
            return new DnsPersistChallengeInfo
            {
                Variant = directory.GetVariant(),
                DirectoryIssuerDomainNames = directory.GetKnownIssuerDomainNames(),
                AccountHashPrefix = directory.AccountHashPrefix,
                AccountUrl = directory.AccountUri,
                AccountThumbprint = directory.AccountThumbprint
            };
        }
    }

    public class DnsPersistRecordOptions
    {
        public DnsPersistVariant Variant { get; set; }

        /// <summary>
        /// The identifier the record is for. A wildcard uses the record of its base domain.
        /// </summary>
        public string Domain { get; set; } = string.Empty;

        public string? IssuerDomainName { get; set; }
        public string? AccountUrl { get; set; }
        public string? AccountThumbprint { get; set; }
        public string? AccountHashPrefix { get; set; }

        /// <summary>
        /// For draft-02, hash with the domain name "*" so one value can be used for every domain
        /// </summary>
        public bool UseDomainOptOut { get; set; }

        public bool PolicyWildcard { get; set; }

        /// <summary>
        /// Optional UNIX timestamp after which the record is not used for new validations
        /// </summary>
        public long? PersistUntil { get; set; }
    }

    public class DnsPersistRecord
    {
        /// <summary>
        /// The domain being validated, i.e. the record name without its _validation-persist label
        /// </summary>
        public string Domain { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// The value split into the character-strings of at most 255 octets a TXT record holds
        /// </summary>
        public List<string> ValueStrings { get; set; } = new List<string>();

        public string AccountUri { get; set; } = string.Empty;
        public string IssuerDomainName { get; set; } = string.Empty;

        public string ZoneFileEntry => DnsPersistRecordBuilder.FormatZoneFileEntry(Name, ValueStrings);
    }

    /// <summary>
    /// Builds _validation-persist TXT records for the dns-persist-01 challenge (draft-ietf-acme-dns-persist), in the
    /// form required by whichever draft a CA follows
    /// </summary>
    public static class DnsPersistRecordBuilder
    {
        public const string RecordLabel = "_validation-persist";
        public const string SpecificationUrl = "https://datatracker.ietf.org/doc/draft-ietf-acme-dns-persist/";
        public const string HashAlgorithm = "sha-256";
        public const string DomainOptOut = "*";
        public const int MaxIssuerDomainNames = 10;
        public const int MaxTxtStringLength = 255;

        /// <summary>
        /// Shown in guidance in place of an issuer domain name when the CA does not publish one before an order
        /// </summary>
        public const string IssuerDomainNamePlaceholder = "<issuer-domain-name>";

        /// <summary>
        /// A CA which publishes a hash prefix in its directory follows draft-02, otherwise records use the plain account URL
        /// </summary>
        public static DnsPersistVariant DetectVariant(string? accountHashPrefix)
        {
            return string.IsNullOrWhiteSpace(accountHashPrefix) ? DnsPersistVariant.PlainAccountUrl : DnsPersistVariant.HashedAccountUri;
        }

        /// <summary>
        /// Lowercase A-label form with no trailing dot, as used for issuer domain names and the hashed domain name
        /// </summary>
        public static string NormalizeDomainName(string? name)
        {
            var normalized = (name ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

            if (normalized.Length == 0 || normalized == DomainOptOut)
            {
                return normalized;
            }

            try
            {
                return new IdnMapping().GetAscii(normalized.Normalize(NormalizationForm.FormC)).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return normalized;
            }
        }

        public static List<string> NormalizeDomainNames(IEnumerable<string>? names)
        {
            return (names ?? Enumerable.Empty<string>())
                .Select(NormalizeDomainName)
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The domain whose _validation-persist record validates an identifier: a wildcard uses its base domain
        /// </summary>
        public static string GetValidationDomain(string identifier)
        {
            var domain = (identifier ?? string.Empty).Trim();

            if (domain.StartsWith("**.", StringComparison.Ordinal))
            {
                domain = domain.Substring(3);
            }
            else if (domain.StartsWith("*.", StringComparison.Ordinal))
            {
                domain = domain.Substring(2);
            }

            return NormalizeDomainName(domain);
        }

        public static string GetRecordName(string identifier) => $"{RecordLabel}.{GetValidationDomain(identifier)}";

        public static bool IsWildcard(string identifier) => (identifier ?? string.Empty).Trim().StartsWith("*", StringComparison.Ordinal);

        /// <summary>
        /// Group identifiers by the record that validates them. A record needs policy=wildcard if any of its identifiers is a wildcard.
        /// </summary>
        public static List<(string Domain, bool RequiresWildcard)> GroupByRecord(IEnumerable<string> identifiers)
        {
            return identifiers
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .GroupBy(GetValidationDomain, StringComparer.Ordinal)
                .Where(g => g.Key.Length > 0)
                .Select(g => (g.Key, g.Any(IsWildcard)))
                .ToList();
        }

        /// <summary>
        /// The account key thumbprint in unpadded base64url form, from the hex fingerprint stored with an account
        /// (e.g. "SHA256 56:3E:CF:..."). Null if the fingerprint is not a SHA-256 hex value.
        /// </summary>
        public static string? GetThumbprintFromFingerprint(string? fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                return null;
            }

            var hex = fingerprint!.Trim();
            if (hex.StartsWith("SHA256", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex.Substring(6);
            }

            hex = new string(hex.Where(Uri.IsHexDigit).ToArray());

            if (hex.Length != 64)
            {
                return null;
            }

            var bytes = new byte[32];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return ToBase64Url(bytes);
        }

        /// <summary>
        /// The draft-02 hashed account URI: accountHashPrefix + "sha-256/" + base64url(SHA-256(length || domain || key || account URL))
        /// </summary>
        /// <param name="domainName">the domain the record is published for, or "*" for one value usable for every domain</param>
        public static string ComputeHashedAccountUri(string accountHashPrefix, string accountUrl, string accountThumbprint, string domainName)
        {
            var domain = Encoding.ASCII.GetBytes(NormalizeDomainName(domainName));

            if (domain.Length == 0 || domain.Length > 255)
            {
                throw new ArgumentException("A domain name is required for the hashed account URI.", nameof(domainName));
            }

            var input = new List<byte> { (byte)domain.Length };
            input.AddRange(domain);
            input.AddRange(Encoding.ASCII.GetBytes(accountThumbprint));
            input.AddRange(Encoding.ASCII.GetBytes(accountUrl));

            using (var sha256 = SHA256.Create())
            {
                return $"{accountHashPrefix}{HashAlgorithm}/{ToBase64Url(sha256.ComputeHash(input.ToArray()))}";
            }
        }

        /// <summary>
        /// The accounturi value a record must hold for the given variant, or null if the inputs it needs are missing
        /// </summary>
        public static string? GetAccountUri(DnsPersistRecordOptions options)
        {
            if (options.Variant == DnsPersistVariant.PlainAccountUrl)
            {
                return options.AccountUrl;
            }

            if (string.IsNullOrWhiteSpace(options.AccountHashPrefix) || string.IsNullOrWhiteSpace(options.AccountUrl) || string.IsNullOrWhiteSpace(options.AccountThumbprint))
            {
                return null;
            }

            return ComputeHashedAccountUri(options.AccountHashPrefix!, options.AccountUrl!, options.AccountThumbprint!, options.UseDomainOptOut ? DomainOptOut : GetValidationDomain(options.Domain));
        }

        public static DnsPersistRecord Build(DnsPersistRecordOptions options)
        {
            var issuer = NormalizeDomainName(options.IssuerDomainName);
            if (issuer.Length == 0)
            {
                issuer = IssuerDomainNamePlaceholder;
            }

            var accountUri = GetAccountUri(options) ?? "<account-uri>";

            var value = $"{issuer}; accounturi={accountUri}";

            if (options.PolicyWildcard)
            {
                value += "; policy=wildcard";
            }

            if (options.PersistUntil.HasValue)
            {
                value += $"; persistUntil={options.PersistUntil.Value.ToString(CultureInfo.InvariantCulture)}";
            }

            var domain = GetValidationDomain(options.Domain);

            return new DnsPersistRecord
            {
                Domain = domain,
                Name = $"{RecordLabel}.{domain}",
                Value = value,
                ValueStrings = SplitTxtValue(value),
                AccountUri = accountUri,
                IssuerDomainName = issuer
            };
        }

        /// <summary>
        /// Split a TXT value into character-strings of at most 255 octets, splitting only between characters
        /// </summary>
        public static List<string> SplitTxtValue(string value, int maxLength = MaxTxtStringLength)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            var currentLength = 0;

            foreach (var element in EnumerateTextElements(value ?? string.Empty))
            {
                var length = Encoding.UTF8.GetByteCount(element);
                if (currentLength + length > maxLength && currentLength > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    currentLength = 0;
                }

                current.Append(element);
                currentLength += length;
            }

            if (currentLength > 0 || parts.Count == 0)
            {
                parts.Add(current.ToString());
            }

            return parts;
        }

        public static string FormatZoneFileEntry(string name, IList<string> valueStrings)
        {
            var quoted = valueStrings.Select(s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"").ToList();
            var rdata = quoted.Count == 1 ? quoted[0] : $"({string.Join(" ", quoted)})";
            return $"{name}. IN TXT {rdata}";
        }

        /// <summary>
        /// Check the issuer domain names of a challenge (C) against the directory's issuerDomainNames (D) and
        /// caaIdentities (I). Returns a description of the problem, or null if the challenge can be used.
        /// </summary>
        public static string? ValidateIssuerDomainNames(IList<string>? challengeNames, IList<string>? directoryNames, IList<string>? caaIdentities)
        {
            if (challengeNames == null || !challengeNames.Any(n => !string.IsNullOrWhiteSpace(n)))
            {
                return "The CA's dns-persist-01 challenge has no issuer domain names, so it cannot be used.";
            }

            if (challengeNames.Count > MaxIssuerDomainNames)
            {
                return $"The CA's dns-persist-01 challenge lists {challengeNames.Count} issuer domain names, more than the {MaxIssuerDomainNames} allowed, so it cannot be used.";
            }

            var c = NormalizeDomainNames(challengeNames);

            var d = NormalizeDomainNames(directoryNames);
            if (d.Count > 0 && d.Count <= MaxIssuerDomainNames)
            {
                var missing = d.Where(n => !c.Contains(n)).ToList();
                if (missing.Any())
                {
                    return $"The CA's dns-persist-01 challenge does not include the issuer domain names its directory lists ({string.Join(", ", missing)}), so it cannot be used.";
                }
            }

            var i = NormalizeDomainNames(caaIdentities);
            if (i.Count > 0)
            {
                var undisclosed = c.Where(n => !i.Contains(n)).ToList();
                if (undisclosed.Any())
                {
                    return $"The CA's dns-persist-01 challenge includes issuer domain names missing from its CAA identities ({string.Join(", ", undisclosed)}), so it cannot be used.";
                }
            }

            return null;
        }

        /// <summary>
        /// Choose the issuer domain name for a new record: one already used by a record in DNS, then one the CA's
        /// directory lists, then the first after sorting. Null when no name is known.
        /// </summary>
        public static string? ChooseIssuerDomainName(IList<string>? challengeNames, IList<string>? directoryNames, IEnumerable<string>? issuersInDns)
        {
            var c = NormalizeDomainNames(challengeNames);
            var d = NormalizeDomainNames(directoryNames);
            var acceptable = c.Any() ? c : d;

            var existing = NormalizeDomainNames(issuersInDns).FirstOrDefault(acceptable.Contains);
            if (existing != null)
            {
                return existing;
            }

            var fromDirectory = d.Where(acceptable.Contains).OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
            if (fromDirectory != null)
            {
                return fromDirectory;
            }

            return acceptable.OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>
        /// Guidance records for the identifiers of a certificate (or a domain match rule), one per record name, for
        /// the given account and CA directory information
        /// </summary>
        /// <param name="preferredIssuerDomainName">the issuer the user picked, used if it is one the CA publishes</param>
        /// <param name="useDomainOptOut">one hashed value for every domain, which only applies to a draft-02 CA</param>
        public static List<DnsPersistRecord> BuildGuidanceRecords(IEnumerable<string> identifiers, DnsPersistDirectoryInfo? directory, string? accountUrl, string? preferredIssuerDomainName, bool policyWildcard, long? persistUntil, bool useDomainOptOut)
        {
            var variant = directory?.GetVariant() ?? DnsPersistVariant.PlainAccountUrl;

            var issuers = directory?.GetKnownIssuerDomainNames() ?? new List<string>();
            var issuer = preferredIssuerDomainName != null && issuers.Contains(preferredIssuerDomainName)
                ? preferredIssuerDomainName
                : ChooseIssuerDomainName(null, issuers, null);

            return GroupByRecord(identifiers)
                .Select(g => Build(new DnsPersistRecordOptions
                {
                    Variant = variant,
                    Domain = g.Domain,
                    IssuerDomainName = issuer,
                    AccountUrl = accountUrl,
                    AccountThumbprint = directory?.AccountThumbprint,
                    AccountHashPrefix = directory?.AccountHashPrefix,
                    UseDomainOptOut = useDomainOptOut && variant == DnsPersistVariant.HashedAccountUri,
                    PolicyWildcard = policyWildcard || g.RequiresWildcard,
                    PersistUntil = persistUntil
                }))
                .ToList();
        }

        /// <summary>
        /// What the guidance records are, for the CA directory of the selected account
        /// </summary>
        public static string GetGuidanceDescription(DnsPersistDirectoryInfo? directory, bool isLoading)
        {
            if (isLoading)
            {
                return "Reading the CA's ACME directory...";
            }

            if (directory == null || !directory.IsDirectoryAvailable)
            {
                return "The CA's ACME directory could not be read, so these records use the plain account URL and may need the CA's issuer domain name. Create one TXT record for each name below.";
            }

            if (directory.GetVariant() == DnsPersistVariant.HashedAccountUri)
            {
                return "This CA uses hashed account values (draft-02), so the account URL is not published in DNS. Create one TXT record for each name below. Records made before an account key change keep working.";
            }

            return "This CA uses the plain account URL in records (draft-00/01). Create one TXT record for each name below. If the CA later moves to hashed account values (draft-02), these records will need replacing.";
        }

        /// <summary>
        /// The identifiers guidance is shown for: those of the certificate, otherwise those of a domain match rule, otherwise an example domain
        /// </summary>
        public static List<string> GetGuidanceIdentifiers(ManagedCertificate? managedCertificate, string? domainMatchRule)
        {
            var identifiers = managedCertificate?.RequestConfig?
                .GetCertificateIdentifiers()
                .Where(i => i.IdentifierType == CertIdentifierType.Dns && !string.IsNullOrWhiteSpace(i.Value))
                .Select(i => i.Value)
                .ToList();

            if (identifiers?.Any() == true)
            {
                return identifiers;
            }

            var matchRuleDomains = (domainMatchRule ?? string.Empty)
                .Split(new[] { "\r\n", "\n", ";" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Where(r => r.Length > 0)
                .ToList();

            return matchRuleDomains.Any() ? matchRuleDomains : new List<string> { "example.com" };
        }

        /// <summary>
        /// The date a persistUntil timestamp falls on (UTC), for date pickers showing it
        /// </summary>
        public static DateTime? GetPersistUntilDate(long? persistUntil)
        {
            return persistUntil.HasValue ? DateTimeOffset.FromUnixTimeSeconds(persistUntil.Value).UtcDateTime.Date : (DateTime?)null;
        }

        /// <summary>
        /// The persistUntil timestamp for a picked date: the start of that day in UTC
        /// </summary>
        public static long? GetPersistUntil(DateTime? date)
        {
            return date.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc)).ToUnixTimeSeconds() : (long?)null;
        }

        private static IEnumerable<string> EnumerateTextElements(string value)
        {
            var e = StringInfo.GetTextElementEnumerator(value);
            while (e.MoveNext())
            {
                yield return e.GetTextElement();
            }
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }

    /// <summary>
    /// A TXT value parsed as an RFC 8659 issue-value
    /// </summary>
    public class DnsPersistIssueValue
    {
        public string Raw { get; set; } = string.Empty;
        public string IssuerDomainName { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Why the record is malformed, if it is
        /// </summary>
        public string? Error { get; set; }

        public string? AccountUri => Parameters.TryGetValue("accounturi", out var v) ? v : null;

        public bool IsWildcardPolicy => Parameters.TryGetValue("policy", out var v) && string.Equals(v, "wildcard", StringComparison.OrdinalIgnoreCase);

        public long? PersistUntil => Parameters.TryGetValue("persistUntil", out var v) && long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var t) ? t : (long?)null;

        public static DnsPersistIssueValue Parse(string txt)
        {
            var result = new DnsPersistIssueValue { Raw = txt ?? string.Empty };

            var parts = result.Raw.Split(';');
            result.IssuerDomainName = DnsPersistRecordBuilder.NormalizeDomainName(parts[0]);

            foreach (var part in parts.Skip(1).Select(p => p.Trim()))
            {
                if (part.Length == 0)
                {
                    continue;
                }

                var separator = part.IndexOf('=');
                if (separator <= 0)
                {
                    result.Error ??= $"the parameter '{part}' is not in tag=value form";
                    continue;
                }

                var tag = part.Substring(0, separator).Trim();
                var value = part.Substring(separator + 1).Trim();

                if (result.Parameters.ContainsKey(tag))
                {
                    result.Error ??= $"the parameter '{tag}' appears more than once";
                    continue;
                }

                result.Parameters[tag] = value;
            }

            if (result.IssuerDomainName.Length == 0)
            {
                result.Error ??= "it has no issuer domain name";
            }
            else if (string.IsNullOrEmpty(result.AccountUri))
            {
                result.Error ??= "it has no accounturi parameter";
            }
            else if (result.Parameters.TryGetValue("persistUntil", out var persistUntil) && result.PersistUntil == null)
            {
                result.Error ??= $"persistUntil '{persistUntil}' is not a UNIX timestamp";
            }

            return result;
        }
    }

    public class DnsPersistRecordCheck
    {
        public string RecordName { get; set; } = string.Empty;
        public bool IsMatch { get; set; }
        public DnsPersistIssueValue? MatchedRecord { get; set; }

        /// <summary>
        /// Why each record found does not satisfy the challenge
        /// </summary>
        public List<string> Problems { get; set; } = new List<string>();

        /// <summary>
        /// Points worth knowing that do not stop the record being used, such as a persistUntil date coming up
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Acceptable issuer domain names already used by records at this name
        /// </summary>
        public List<string> IssuersInDns { get; set; } = new List<string>();
    }

    /// <summary>
    /// Checks the TXT records found at a _validation-persist name against what a CA will accept
    /// </summary>
    public static class DnsPersistRecordChecker
    {
        public static readonly TimeSpan PersistUntilWarningPeriod = TimeSpan.FromDays(14);

        public static DnsPersistRecordCheck Check(IEnumerable<string>? txtValues, DnsPersistChallengeInfo challenge, string identifier, bool requiresWildcard, DateTimeOffset now)
        {
            var domain = DnsPersistRecordBuilder.GetValidationDomain(identifier);
            var check = new DnsPersistRecordCheck { RecordName = $"{DnsPersistRecordBuilder.RecordLabel}.{domain}" };

            var values = (txtValues ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (!values.Any())
            {
                check.Problems.Add($"No TXT record was found at {check.RecordName}.");
                return check;
            }

            var challengeNames = DnsPersistRecordBuilder.NormalizeDomainNames(challenge.IssuerDomainNames);
            var directoryNames = DnsPersistRecordBuilder.NormalizeDomainNames(challenge.DirectoryIssuerDomainNames);
            var acceptableIssuers = challengeNames.Any() ? challengeNames : directoryNames;

            var expected = GetAcceptableAccountUris(challenge, domain);
            var otherIssuers = new List<string>();

            foreach (var record in values.Select(DnsPersistIssueValue.Parse))
            {
                if (acceptableIssuers.Any() && !acceptableIssuers.Contains(record.IssuerDomainName))
                {
                    // a record for another CA, which this CA ignores
                    otherIssuers.Add(record.IssuerDomainName.Length > 0 ? record.IssuerDomainName : $"\"{record.Raw}\"");
                    continue;
                }

                check.IssuersInDns.Add(record.IssuerDomainName);

                var prefix = $"The record for {record.IssuerDomainName}";

                if (record.Error != null)
                {
                    check.Problems.Add($"{prefix} is malformed: {record.Error}.");
                    continue;
                }

                var accountProblem = GetAccountUriProblem(record.AccountUri!, challenge, expected);
                if (accountProblem != null)
                {
                    check.Problems.Add($"{prefix} {accountProblem}");
                    continue;
                }

                if (requiresWildcard && !record.IsWildcardPolicy)
                {
                    check.Problems.Add($"{prefix} has no policy=wildcard, which wildcard identifiers such as *.{domain} need.");
                    continue;
                }

                if (record.PersistUntil.HasValue)
                {
                    var until = DateTimeOffset.FromUnixTimeSeconds(record.PersistUntil.Value);
                    if (until <= now)
                    {
                        check.Problems.Add($"{prefix} expired at {until:u} (persistUntil), so the CA will no longer use it.");
                        continue;
                    }

                    if (until - now < PersistUntilWarningPeriod)
                    {
                        check.Warnings.Add($"{prefix} expires at {until:u} (persistUntil). Replace it before then to keep renewing.");
                    }
                }

                if (!acceptableIssuers.Any())
                {
                    check.Warnings.Add($"The CA does not publish its issuer domain names, so the CA must confirm that {record.IssuerDomainName} is one of them.");
                }

                // one acceptable record is enough, whatever is wrong with the others
                check.IsMatch = true;
                check.MatchedRecord = record;
                check.Problems.Clear();
                return check;
            }

            if (otherIssuers.Any() && !check.IssuersInDns.Any())
            {
                check.Problems.Add($"The TXT records at {check.RecordName} are for other issuers ({string.Join(", ", otherIssuers.Distinct())}). This CA accepts {string.Join(", ", acceptableIssuers)}.");
            }

            return check;
        }

        private static List<string> GetAcceptableAccountUris(DnsPersistChallengeInfo challenge, string domain)
        {
            var options = new DnsPersistRecordOptions
            {
                Variant = challenge.Variant,
                Domain = domain,
                AccountUrl = challenge.AccountUrl,
                AccountThumbprint = challenge.AccountThumbprint,
                AccountHashPrefix = challenge.AccountHashPrefix
            };

            var uris = new List<string?> { DnsPersistRecordBuilder.GetAccountUri(options) };

            if (challenge.Variant == DnsPersistVariant.HashedAccountUri)
            {
                options.UseDomainOptOut = true;
                uris.Add(DnsPersistRecordBuilder.GetAccountUri(options));
            }

            return uris.Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).Distinct().ToList();
        }

        private static string? GetAccountUriProblem(string accountUri, DnsPersistChallengeInfo challenge, List<string> expected)
        {
            if (expected.Contains(accountUri, StringComparer.Ordinal))
            {
                return null;
            }

            if (challenge.Variant == DnsPersistVariant.HashedAccountUri)
            {
                if (!expected.Any())
                {
                    return "cannot be checked, because the account's key thumbprint is not known.";
                }

                if (string.Equals(accountUri, challenge.AccountUrl, StringComparison.Ordinal))
                {
                    return "uses the plain account URL, but this CA requires the hashed account URI (draft-02). Replace the record with the value given here.";
                }

                if (accountUri.StartsWith(challenge.AccountHashPrefix ?? string.Empty, StringComparison.Ordinal))
                {
                    // the CA keeps accepting hashes made with the account's earlier keys, which this instance does not know
                    return "has a hashed account URI which is not this account's for its current key. If the record was made before an account key change the CA still accepts it, otherwise it is for a different account and needs replacing with the value given here.";
                }

                return $"has accounturi={accountUri}, which is not a hashed account URI under this CA's prefix {challenge.AccountHashPrefix}.";
            }

            if (IsHashedAccountUri(accountUri))
            {
                return $"uses a hashed account URI, but this CA expects the plain account URL {expected.FirstOrDefault()}.";
            }

            return $"is for a different ACME account (accounturi={accountUri}). This account is {expected.FirstOrDefault()}.";
        }

        private static bool IsHashedAccountUri(string accountUri)
        {
            var marker = $"/{DnsPersistRecordBuilder.HashAlgorithm}/";
            var index = accountUri.LastIndexOf(marker, StringComparison.Ordinal);
            return index > 0 && accountUri.Length - (index + marker.Length) == 43;
        }
    }
}
