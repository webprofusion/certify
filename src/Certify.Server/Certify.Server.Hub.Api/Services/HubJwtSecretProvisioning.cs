using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// The outcome of checking a settings file for a usable JWT signing secret.
    /// </summary>
    public enum JwtSecretProvisioningOutcome
    {
        /// <summary>
        /// A secret was already present, the file was not modified.
        /// </summary>
        AlreadyPresent,

        /// <summary>
        /// The shipped placeholder was replaced with a generated secret.
        /// </summary>
        PlaceholderReplaced,

        /// <summary>
        /// No secret was configured, so one was generated and saved.
        /// </summary>
        SecretGenerated,

        /// <summary>
        /// The file could not be read, parsed or written. The secret is unchanged.
        /// </summary>
        Failed
    }

    /// <summary>
    /// The result of a provisioning attempt, including a description suitable for reporting as system status.
    /// </summary>
    public sealed class JwtSecretProvisioningResult
    {
        public JwtSecretProvisioningOutcome Outcome { get; init; }
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// Path the previous version of the file was copied to, when the file had to be rewritten.
        /// </summary>
        public string? BackupPath { get; init; }
    }

    /// <summary>
    /// Ensures the hub settings file holds a usable JWT signing secret.
    ///
    /// No secret ships in appsettings.json, because a signing key published with the product would let anyone forge a
    /// token for any security principal. That means a settings file which pre-dates the secret, or has had it removed,
    /// would otherwise leave the service unable to issue or validate tokens at all, so one is generated and saved here.
    /// </summary>
    public static class HubJwtSecretProvisioning
    {
        /// <summary>
        /// Placeholder used in the shipped default settings file, replaced on first run.
        /// </summary>
        public const string SecretPlaceholder = "<replace jwt secret>";

        private const string JwtSettingsSectionName = "JwtSettings";
        private const string SecretPropertyName = "secret";

        /// <summary>
        /// Generate a new signing secret: 32 bytes from a cryptographic RNG, base64 encoded.
        /// </summary>
        public static string GenerateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        /// <summary>
        /// Check the given settings file for a JWT signing secret, generating and saving one if it has none.
        /// </summary>
        /// <param name="configPath">Path to the hub settings file (hubservice.json).</param>
        public static JwtSecretProvisioningResult EnsureSecret(string configPath)
        {
            try
            {
                var content = File.ReadAllText(configPath);

                // Handled as a text replacement so the comments in the shipped default settings (the commented out
                // Kestrel HTTPS example) survive. Re-serializing the document would discard them.
                if (content.Contains(SecretPlaceholder, StringComparison.Ordinal))
                {
                    File.WriteAllText(configPath, content.Replace(SecretPlaceholder, GenerateSecret()));

                    return new JwtSecretProvisioningResult
                    {
                        Outcome = JwtSecretProvisioningOutcome.PlaceholderReplaced,
                        Message = $"Generated a new JWT signing secret in {configPath}, replacing the default placeholder."
                    };
                }

                if (HubSettingsFile.Parse(content) is not JsonObject root)
                {
                    return new JwtSecretProvisioningResult
                    {
                        Outcome = JwtSecretProvisioningOutcome.Failed,
                        Message = $"Could not read JWT settings from {configPath}, the file is not a JSON object."
                    };
                }

                if (!string.IsNullOrWhiteSpace(GetSecretValue(root[JwtSettingsSectionName] as JsonObject)))
                {
                    return new JwtSecretProvisioningResult
                    {
                        Outcome = JwtSecretProvisioningOutcome.AlreadyPresent,
                        Message = "A JWT signing secret is already configured."
                    };
                }

                HubSettingsFile.EnsureSection(root, JwtSettingsSectionName)[SecretPropertyName] = GenerateSecret();

                var backupPath = HubSettingsFile.WriteWithBackup(configPath, root);

                var backupNote = backupPath != null ? $" The previous file was copied to {backupPath}." : string.Empty;

                return new JwtSecretProvisioningResult
                {
                    Outcome = JwtSecretProvisioningOutcome.SecretGenerated,
                    BackupPath = backupPath,
                    Message = $"No JWT signing secret was configured, so a new one has been generated and saved to {configPath}. Any previously issued tokens are no longer valid.{backupNote}"
                };
            }
            catch (Exception ex)
            {
                // The caller continues startup, so that a settings file which cannot be read or written surfaces as a
                // clear authentication configuration error rather than an opaque file error at this point.
                return new JwtSecretProvisioningResult
                {
                    Outcome = JwtSecretProvisioningOutcome.Failed,
                    Message = $"Could not check or update the JWT signing secret in {configPath} - {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Read the secret value, tolerating a non-string value (which is not usable as a secret).
        /// </summary>
        private static string? GetSecretValue(JsonObject? jwtSettings)
        {
            if (jwtSettings?[SecretPropertyName] is not JsonValue secretValue)
            {
                return null;
            }

            return secretValue.TryGetValue<string>(out var secret) ? secret : null;
        }
    }
}
