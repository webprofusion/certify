using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Certify.Models.Hub
{
    public static class ManagedInstanceRequestAuth
    {
        public const string HubAssignedIdHeaderName = "X-Certify-HubAssignedId";
        public const string InstanceVersionHeaderName = "X-Certify-InstanceVersion";
        public const string TimestampHeaderName = "X-Certify-Timestamp";
        public const string SignatureHeaderName = "X-Certify-Signature";

        public const string CachedBodyHashItemKey = "Certify.ManagedInstanceRequestAuth.BodyHash";

        public static readonly TimeSpan DefaultAllowedClockSkew = TimeSpan.FromMinutes(5);

        public static string GenerateSecret(int numBytes = 32)
        {
            if (numBytes <= 0)
            {
                numBytes = 32;
            }

            var bytes = new byte[numBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        public static string DeriveSecretHash(string secret)
        {
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret ?? string.Empty)));
            }
        }

        public static string ComputeBodyHash(byte[]? bodyBytes)
        {
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(bodyBytes ?? Array.Empty<byte>()));
            }
        }

        public static string BuildPayload(string instanceId, string timestamp, string httpMethod, string requestPathAndQuery, string bodyHash)
        {
            var normalizedPath = string.IsNullOrWhiteSpace(requestPathAndQuery) ? "/" : requestPathAndQuery;
            var normalizedMethod = string.IsNullOrWhiteSpace(httpMethod) ? "GET" : httpMethod.ToUpperInvariant();

            return string.Join("|",
                instanceId ?? string.Empty,
                timestamp ?? string.Empty,
                normalizedMethod,
                normalizedPath,
                bodyHash ?? string.Empty);
        }

        public static string ComputeSignatureFromSecret(string secret, string instanceId, string timestamp, string httpMethod, string requestPathAndQuery, string bodyHash)
        {
            return ComputeSignatureFromSecretHash(DeriveSecretHash(secret), instanceId, timestamp, httpMethod, requestPathAndQuery, bodyHash);
        }

        public static string ComputeSignatureFromSecretHash(string secretHash, string instanceId, string timestamp, string httpMethod, string requestPathAndQuery, string bodyHash)
        {
            var payload = BuildPayload(instanceId, timestamp, httpMethod, requestPathAndQuery, bodyHash);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var keyBytes = Convert.FromBase64String(secretHash ?? string.Empty);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                return Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
            }
        }

        public static bool TryParseTimestamp(string timestamp, out DateTimeOffset value)
        {
            return DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
        }

        public static bool FixedTimeEquals(string? left, string? right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            var leftBytes = GetComparisonBytes(left);
            var rightBytes = GetComparisonBytes(right);

            var diff = leftBytes.Length ^ rightBytes.Length;
            var maxLength = Math.Max(leftBytes.Length, rightBytes.Length);

            for (var i = 0; i < maxLength; i++)
            {
                var leftByte = i < leftBytes.Length ? leftBytes[i] : (byte)0;
                var rightByte = i < rightBytes.Length ? rightBytes[i] : (byte)0;
                diff |= leftByte ^ rightByte;
            }

            return diff == 0;
        }

        private static byte[] GetComparisonBytes(string value)
        {
            try
            {
                return Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                return Encoding.UTF8.GetBytes(value);
            }
        }
    }
}
