using System.Security.Cryptography;
using Certify.Models;
using Certify.Server.Hub.Api.Models.Acme;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Helper service for ACME-related operations and data generation
    /// </summary>
    public class AcmeHelper
    {
        private const int DEFAULT_EXPIRY_DAYS = 30;
        private const int NONCE_BYTES = 16;
        private const int TOKEN_BYTES = 32;

        private readonly AcmeServerConfig _config;

        public AcmeHelper(AcmeServerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Generates a unique account identifier
        /// </summary>
        public static string GenerateAccountId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Generates a unique order identifier
        /// </summary>
        public static string GenerateOrderId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Generates a unique authorization identifier
        /// </summary>
        public static string GenerateAuthorizationId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Generates a unique challenge identifier
        /// </summary>
        public static string GenerateChallengeId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Generates a unique certificate identifier
        /// </summary>
        public static string GenerateCertificateId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Generates a random token for challenges
        /// </summary>
        public static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TOKEN_BYTES));

        /// <summary>
        /// Builds the base URL for ACME endpoints
        /// </summary>
        public static string BuildBaseUrl(HttpRequest request, string? key = null) =>
            $"{request.Scheme}://{request.Host}/acme{(key != null ? $"/{key}" : "")}";

        /// <summary>
        /// Builds an account URL
        /// </summary>
        public static string BuildAccountUrl(string baseUrl, string accountId) => $"{baseUrl}/account/{accountId}";

        /// <summary>
        /// Builds a challenge URL
        /// </summary>
        public static string BuildChallengeUrl(string baseUrl, string challengeId) => $"{baseUrl}/challenge/{challengeId}";

        /// <summary>
        /// Builds an authorization URL
        /// </summary>
        public static string BuildAuthorizationUrl(string baseUrl, string authId) => $"{baseUrl}/authz/{authId}";

        /// <summary>
        /// Builds an order URL
        /// </summary>
        public static string BuildOrderUrl(string baseUrl, string orderId) => $"{baseUrl}/order/{orderId}";

        /// <summary>
        /// Builds a certificate URL
        /// </summary>
        public static string BuildCertificateUrl(string baseUrl, string certId) => $"{baseUrl}/cert/{certId}";

        /// <summary>
        /// Creates standard challenges for an authorization
        /// </summary>
        public List<AcmeChallenge> CreateStandardChallenges(string baseUrl)
        {
            return
            [
                new()
                {
                    Type = "http-01",
                    Status = ChallengeStatus.Valid,
                    Token = GenerateToken(),
                    Url = BuildChallengeUrl(baseUrl, GenerateChallengeId())
                },
                new()
                {
                    Type = "dns-01",
                    Status = ChallengeStatus.Valid,
                    Token = GenerateToken(),
                    Url = BuildChallengeUrl(baseUrl, GenerateChallengeId())
                }
            ];
        }

        /// <summary>
        /// Creates an authorization object for an identifier
        /// </summary>
        public AcmeAuthorization CreateAuthorization(AcmeIdentifier identifier, string baseUrl)
        {
            return new()
            {
                Identifier = identifier,
                Status = AuthorizationStatus.Valid, //presets auth to valid so the client doesn't attempt them
                Expires = DateTime.UtcNow.AddDays(DEFAULT_EXPIRY_DAYS),
                Challenges = CreateStandardChallenges(baseUrl)
            };
        }

        /// <summary>
        /// Prepares a managed certificate from an ACME order request
        /// </summary>
        public static ManagedCertificate PrepareManagedCertificate(string orderId, NewOrderRequest request)
        {
            var managedCert = new ManagedCertificate
            {
                Name = $"Hub ACME Order {orderId}",
                CertificateAuthorityId = StandardCertAuthorities.LETS_ENCRYPT,
                RequestConfig = new()
                {
                    PrimaryDomain = request.Identifiers.FirstOrDefault()?.Value ?? "",
                    SubjectAlternativeNames = request.Identifiers.Select(i => i.Value).ToArray(),
                    DeploymentSiteOption = DeploymentOption.NoDeployment
                }
            };

            managedCert.RequestConfig.Challenges =
            [
                new() {
                    ChallengeProvider = "managed" ,
                    ChallengeType = "dns-01",
                }
            ];

            return managedCert;
        }

        /// <summary>
        /// Formats a CSR into PEM format
        /// </summary>
        public static string FormatCsrPem(string csr)
        {
            return $"-----BEGIN CERTIFICATE REQUEST-----\n{Convert.ToBase64String(Certify.Management.Util.FromUrlSafeBase64String(csr), Base64FormattingOptions.InsertLineBreaks)}\n-----END CERTIFICATE REQUEST-----";
        }

        /// <summary>
        /// Generates a cryptographically secure nonce
        /// </summary>
        public async Task<string> GenerateNonce()
        {
            var nonce = JwsConvert.ToBase64String(RandomNumberGenerator.GetBytes(NONCE_BYTES));
            var timestamp = DateTime.UtcNow.ToString();

            await _config.StoreAcmeNonce(nonce, timestamp);

            return nonce;
        }

        /// <summary>
        /// Validates if a key is properly formatted (placeholder implementation)
        /// </summary>
        public bool ValidateKeyIfSupplied(string key)
        {
            // TODO: Implement key validation logic if needed
            return true;
        }

        /// <summary>
        /// Creates an ACME error response
        /// </summary>
        public static AcmeError CreateAcmeErrorObject(string errorType, string detail)
        {
            return new AcmeError { Type = errorType, Detail = detail };
        }
    }
}
