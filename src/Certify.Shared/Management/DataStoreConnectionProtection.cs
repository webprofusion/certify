using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Certify.Models.Providers;

namespace Certify.Management
{
    /// <summary>
    /// Protection for data store connection configuration, which commonly contains database credentials.
    /// Values are encrypted at rest and are never sent to a client in cleartext - the client instead receives a
    /// masked version which it can send back unchanged to leave the stored connection details as they are.
    /// </summary>
    public static class DataStoreConnectionProtection
    {
        /// <summary>
        /// Marks a stored value as encrypted. Values without this prefix are legacy cleartext and are
        /// re-protected the next time the connection is saved.
        /// </summary>
        public const string ProtectedPrefix = "enc:v1:";

        /// <summary>
        /// Replaces secret values in the masked version sent to clients.
        /// </summary>
        public const string MaskToken = "********";

        private const string ProtectionEntropy = "datastoreconnection";

        // key=value style secrets, e.g. Password=hunter2;
        private static readonly Regex _keyValueSecretPattern = new Regex(
            @"\b(password|pwd|secret|token|accountkey|sharedaccesskey|api[_ ]?key|access[_ ]?key)(\s*=\s*)([^;]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // uri style credentials, e.g. postgres://user:hunter2@host/db
        private static readonly Regex _uriSecretPattern = new Regex(
            @"(://[^:/?#\s]+:)([^@\s]+)(?=@)",
            RegexOptions.Compiled);

        /// <summary>
        /// True if the given stored value is encrypted, false if it is legacy cleartext.
        /// </summary>
        public static bool IsProtectedValue(string value) => value?.StartsWith(ProtectedPrefix, StringComparison.Ordinal) == true;

        /// <summary>
        /// True if the given value has had secrets masked out and so does not contain the real connection details.
        /// </summary>
        public static bool IsMasked(string value) => value?.Contains(MaskToken) == true;

        /// <summary>
        /// Encrypt a connection configuration for storage. Values which are already encrypted are returned unchanged.
        /// </summary>
        public static string Protect(string connectionConfig, ILog log = null)
        {
            // an empty config is meaningful (the default sqlite store uses one) and has nothing to protect
            if (string.IsNullOrEmpty(connectionConfig) || IsProtectedValue(connectionConfig))
            {
                return connectionConfig;
            }

            try
            {
                return ProtectedPrefix + CredentialsUtil.Protect(connectionConfig, ProtectionEntropy, DataProtectionScope.LocalMachine);
            }
            catch (Exception exp)
            {
                // storing the connection unencrypted is preferable to failing to store it at all, as the service
                // would otherwise lose the connection details for its own data store
                log?.Error($"Failed to encrypt data store connection configuration, storing unencrypted :: {exp.Message}");
                return connectionConfig;
            }
        }

        /// <summary>
        /// Decrypt a stored connection configuration. Legacy cleartext values are returned unchanged.
        /// </summary>
        public static string Unprotect(string storedValue, ILog log = null)
        {
            if (!IsProtectedValue(storedValue))
            {
                // legacy cleartext, upgraded to an encrypted value on next save
                return storedValue;
            }

            try
            {
                return CredentialsUtil.Unprotect(storedValue.Substring(ProtectedPrefix.Length), ProtectionEntropy, DataProtectionScope.LocalMachine);
            }
            catch (Exception exp)
            {
                // the value is most likely encrypted on a different machine. Return it as stored rather than
                // discarding it, so that a subsequent save cannot overwrite a value which is still recoverable
                log?.Error($"Failed to decrypt data store connection configuration [{exp.Message}]. Check whether this configuration was encrypted on another machine.");
                return storedValue;
            }
        }

        /// <summary>
        /// Mask the secrets in a connection configuration, leaving enough detail (such as host and database name)
        /// for an operator to identify the connection without exposing the credentials.
        /// </summary>
        public static string Mask(string connectionConfig)
        {
            if (string.IsNullOrWhiteSpace(connectionConfig))
            {
                return connectionConfig;
            }

            if (IsProtectedValue(connectionConfig))
            {
                // could not be decrypted, so nothing can be shown about it
                return MaskToken;
            }

            var masked = _uriSecretPattern.Replace(connectionConfig, "$1" + MaskToken);

            masked = _keyValueSecretPattern.Replace(masked, m => string.IsNullOrEmpty(m.Groups[3].Value)
                ? m.Value
                : m.Groups[1].Value + m.Groups[2].Value + MaskToken);

            return masked;
        }
    }
}
