using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Certify.Providers;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.Services.Acme;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Validation of ACME account keys: how they are compared for external account binding,
    /// the key policy they must satisfy, and how nonce failures are reported.
    /// </summary>
    [TestClass]
    public class AcmeAccountKeyTests
    {
        private static JsonWebKey CreateEcKey(string curveName = "P-256")
        {
            var curve = curveName switch
            {
                "P-256" => ECCurve.NamedCurves.nistP256,
                "P-384" => ECCurve.NamedCurves.nistP384,
                "P-521" => ECCurve.NamedCurves.nistP521,
                _ => throw new ArgumentException($"Unhandled test curve {curveName}")
            };

            using var ecdsa = ECDsa.Create(curve);
            var parameters = ecdsa.ExportParameters(false);

            return new JsonWebKey
            {
                Kty = "EC",
                Crv = curveName,
                X = JwsConvert.ToBase64String(parameters.Q.X!),
                Y = JwsConvert.ToBase64String(parameters.Q.Y!)
            };
        }

        private static JsonWebKey CreateRsaKey(int keySizeBits = 2048)
        {
            using var rsa = RSA.Create(keySizeBits);
            var parameters = rsa.ExportParameters(false);

            return new JsonWebKey
            {
                Kty = "RSA",
                N = JwsConvert.ToBase64String(parameters.Modulus!),
                E = JwsConvert.ToBase64String(parameters.Exponent!)
            };
        }

        #region key comparison (external account binding)

        [TestMethod, Description("Two different EC account keys must not compare as the same key")]
        public void DifferentEcKeysAreNotTheSameKey()
        {
            var keyA = CreateEcKey();
            var keyB = CreateEcKey();

            Assert.IsFalse(JsonWebKeyThumbprint.IsSameKey(keyA, keyB), "distinct EC keys compared as equal");
        }

        [TestMethod, Description("An EC key must compare as the same key as itself")]
        public void MatchingEcKeysAreTheSameKey()
        {
            var key = CreateEcKey();
            var sameKey = new JsonWebKey { Kty = key.Kty, Crv = key.Crv, X = key.X, Y = key.Y };

            Assert.IsTrue(JsonWebKeyThumbprint.IsSameKey(key, sameKey));
        }

        [TestMethod, Description("EC keys differing only by curve must not compare as the same key")]
        public void EcKeysDifferingByCurveAreNotTheSameKey()
        {
            var key = CreateEcKey();
            var differentCurve = new JsonWebKey { Kty = key.Kty, Crv = "P-384", X = key.X, Y = key.Y };

            Assert.IsFalse(JsonWebKeyThumbprint.IsSameKey(key, differentCurve));
        }

        [TestMethod, Description("Two different RSA account keys must not compare as the same key")]
        public void DifferentRsaKeysAreNotTheSameKey()
        {
            Assert.IsFalse(JsonWebKeyThumbprint.IsSameKey(CreateRsaKey(), CreateRsaKey()));
        }

        [TestMethod, Description("An RSA key must compare as the same key as itself")]
        public void MatchingRsaKeysAreTheSameKey()
        {
            var key = CreateRsaKey();
            var sameKey = new JsonWebKey { Kty = key.Kty, N = key.N, E = key.E };

            Assert.IsTrue(JsonWebKeyThumbprint.IsSameKey(key, sameKey));
        }

        [TestMethod, Description("Keys of different types must not compare as the same key")]
        public void KeysOfDifferentTypesAreNotTheSameKey()
        {
            Assert.IsFalse(JsonWebKeyThumbprint.IsSameKey(CreateEcKey(), CreateRsaKey()));
        }

        [TestMethod, Description("A padded or standard base64 encoded key still matches the same key encoded as base64url")]
        public void EquivalentKeyEncodingsAreTheSameKey()
        {
            using var rsa = RSA.Create(2048);
            var parameters = rsa.ExportParameters(false);

            var urlSafe = new JsonWebKey
            {
                Kty = "RSA",
                N = JwsConvert.ToBase64String(parameters.Modulus!),
                E = JwsConvert.ToBase64String(parameters.Exponent!)
            };

            var standardBase64 = new JsonWebKey
            {
                Kty = "RSA",
                N = Convert.ToBase64String(parameters.Modulus!),
                E = Convert.ToBase64String(parameters.Exponent!)
            };

            Assert.IsTrue(JsonWebKeyThumbprint.IsSameKey(urlSafe, standardBase64));
        }

        [TestMethod, Description("Incomplete or unsupported keys never compare as the same key")]
        public void IncompleteKeysAreNeverTheSameKey()
        {
            var incompleteEc = new JsonWebKey { Kty = "EC", Crv = "P-256", X = CreateEcKey().X };
            var unsupported = new JsonWebKey { Kty = "OKP", Crv = "Ed25519", X = "abc" };

            Assert.IsFalse(JsonWebKeyThumbprint.IsSameKey(incompleteEc, incompleteEc));
            Assert.IsFalse(JsonWebKeyThumbprint.IsSameKey(unsupported, unsupported));
            Assert.IsFalse(JsonWebKeyThumbprint.IsSameKey(null, null));
        }

        [TestMethod, Description("A key member which is not base64url cannot be used to forge a matching thumbprint")]
        public void CraftedKeyMemberDoesNotProduceAThumbprint()
        {
            var crafted = new JsonWebKey { Kty = "EC", Crv = "P-256\",\"kty\":\"EC", X = "AAAA", Y = "BBBB" };

            Assert.IsNull(JsonWebKeyThumbprint.Compute(crafted));
        }

        #endregion

        #region key policy

        [TestMethod, Description("RSA account keys below the minimum size are rejected")]
        public void UndersizedRsaKeyIsRejected()
        {
            var failureReason = AcmeKeyPolicy.ValidateKey(CreateRsaKey(1024));

            Assert.IsNotNull(failureReason);
            StringAssert.Contains(failureReason, "2048");
        }

        [TestMethod, Description("RSA account keys at the minimum size are accepted")]
        public void MinimumSizeRsaKeyIsAccepted()
        {
            Assert.IsNull(AcmeKeyPolicy.ValidateKey(CreateRsaKey(2048)));
        }

        [TestMethod, Description("An RSA key with an unusable public exponent is rejected")]
        public void RsaKeyWithEvenExponentIsRejected()
        {
            var key = CreateRsaKey();
            key.E = JwsConvert.ToBase64String([0x02]);

            Assert.IsNotNull(AcmeKeyPolicy.ValidateKey(key));
        }

        [TestMethod, Description("EC account keys on supported curves are accepted")]
        public void SupportedEcCurvesAreAccepted()
        {
            Assert.IsNull(AcmeKeyPolicy.ValidateKey(CreateEcKey("P-256")));
            Assert.IsNull(AcmeKeyPolicy.ValidateKey(CreateEcKey("P-384")));
            Assert.IsNull(AcmeKeyPolicy.ValidateKey(CreateEcKey("P-521")));
        }

        [TestMethod, Description("EC account keys on unsupported curves are rejected")]
        public void UnsupportedEcCurveIsRejected()
        {
            var key = CreateEcKey();
            key.Crv = "P-192";

            Assert.IsNotNull(AcmeKeyPolicy.ValidateKey(key));
        }

        [TestMethod, Description("EC coordinates which are the wrong width for the curve are rejected")]
        public void EcKeyWithWrongCoordinateSizeIsRejected()
        {
            var key = CreateEcKey();
            key.X = JwsConvert.ToBase64String(new byte[16]);

            Assert.IsNotNull(AcmeKeyPolicy.ValidateKey(key));
        }

        [TestMethod, Description("An ECDSA algorithm may only be used with the curve it is defined for")]
        public void EcAlgorithmMustMatchCurve()
        {
            Assert.IsNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateEcKey("P-256"), "ES256"));
            Assert.IsNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateEcKey("P-384"), "ES384"));
            Assert.IsNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateEcKey("P-521"), "ES512"));

            Assert.IsNotNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateEcKey("P-521"), "ES256"));
            Assert.IsNotNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateEcKey("P-256"), "ES384"));
        }

        [TestMethod, Description("The key type must match the algorithm family")]
        public void KeyTypeMustMatchAlgorithmFamily()
        {
            Assert.IsNotNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateEcKey(), "RS256"));
            Assert.IsNotNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateRsaKey(), "ES256"));

            Assert.IsNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateRsaKey(), "RS256"));
            Assert.IsNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateRsaKey(), "PS384"));
        }

        [TestMethod, Description("HMAC and unsigned algorithms are never accepted for account key signatures")]
        public void HmacAndNoneAlgorithmsAreRejected()
        {
            Assert.IsFalse(AcmeKeyPolicy.IsSupportedSignatureAlgorithm("HS256"));
            Assert.IsFalse(AcmeKeyPolicy.IsSupportedSignatureAlgorithm("none"));
            Assert.IsFalse(AcmeKeyPolicy.IsSupportedSignatureAlgorithm(null));

            Assert.IsNotNull(AcmeKeyPolicy.ValidateKeyForAlgorithm(CreateRsaKey(), "HS256"));
        }

        #endregion

        #region nonce handling

        private static AcmeJwsValidator CreateValidator(out AcmeServerConfig config)
        {
            config = new AcmeServerConfig(new Mock<IConfigurationStore>().Object, "acme-tests");
            return new AcmeJwsValidator(NullLogger<AcmeJwsValidator>.Instance, config);
        }

        private static JwsPayload CreateSignedRequest(ECDsa ecdsa, JsonWebKey accountKey, string requestUrl, string nonce)
        {
            var header = JsonConvert.SerializeObject(new JwsProtectedHeader
            {
                Alg = "ES256",
                Jwk = accountKey,
                Url = requestUrl,
                Nonce = nonce
            }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            var protectedPart = JwsConvert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(header));
            var payloadPart = JwsConvert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"termsOfServiceAgreed\":true}"));

            var signature = ecdsa.SignData(
                System.Text.Encoding.UTF8.GetBytes($"{protectedPart}.{payloadPart}"),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            return new JwsPayload
            {
                Protected = protectedPart,
                Payload = payloadPart,
                Signature = JwsConvert.ToBase64String(signature)
            };
        }

        [TestMethod, Description("A nonce which was never issued is reported as badNonce so the client can retry")]
        public async Task UnknownNonceIsReportedAsBadNonce()
        {
            var validator = CreateValidator(out _);

            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdsa.ExportParameters(false);
            var accountKey = new JsonWebKey
            {
                Kty = "EC",
                Crv = "P-256",
                X = JwsConvert.ToBase64String(parameters.Q.X!),
                Y = JwsConvert.ToBase64String(parameters.Q.Y!)
            };

            var requestUrl = "https://hub.example.com/acme/new-account";
            var payload = CreateSignedRequest(ecdsa, accountKey, requestUrl, "a-nonce-which-was-never-issued");

            var exception = await Assert.ThrowsExactlyAsync<AcmeRequestException>(
                async () => await validator.DecodeJwsPayload<object>(payload, requestUrl, requireAccountKid: false));

            Assert.AreEqual(AcmeErrorResponseService.AcmeErrorTypes.BadNonce, exception.ErrorType);
        }

        [TestMethod, Description("A nonce is single use, so replaying it is reported as badNonce")]
        public async Task ReplayedNonceIsReportedAsBadNonce()
        {
            var validator = CreateValidator(out var config);

            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdsa.ExportParameters(false);
            var accountKey = new JsonWebKey
            {
                Kty = "EC",
                Crv = "P-256",
                X = JwsConvert.ToBase64String(parameters.Q.X!),
                Y = JwsConvert.ToBase64String(parameters.Q.Y!)
            };

            var requestUrl = "https://hub.example.com/acme/new-account";
            var nonce = Guid.NewGuid().ToString("N");
            await config.StoreAcmeNonce(nonce, DateTime.UtcNow);

            var payload = CreateSignedRequest(ecdsa, accountKey, requestUrl, nonce);

            // first use succeeds, which also confirms a normal EC account key passes key policy
            var result = await validator.DecodeJwsPayload<object>(payload, requestUrl, requireAccountKid: false);
            Assert.IsNotNull(result);

            var exception = await Assert.ThrowsExactlyAsync<AcmeRequestException>(
                async () => await validator.DecodeJwsPayload<object>(payload, requestUrl, requireAccountKid: false));

            Assert.AreEqual(AcmeErrorResponseService.AcmeErrorTypes.BadNonce, exception.ErrorType);
        }

        [TestMethod, Description("A badNonce failure maps to a badNonce error response rather than a generic malformed error")]
        public void BadNonceExceptionMapsToBadNonceResponse()
        {
            var result = AcmeErrorResponseService.CreateAcmeErrorForException(
                new AcmeRequestException(AcmeErrorResponseService.AcmeErrorTypes.BadNonce, "Invalid or expired nonce in JWS header"),
                "Invalid JWS payload");

            var objectResult = result as Microsoft.AspNetCore.Mvc.ObjectResult;

            Assert.IsNotNull(objectResult);
            Assert.AreEqual(400, objectResult.StatusCode);
            StringAssert.Contains(JsonConvert.SerializeObject(objectResult.Value), "badNonce");
        }

        [TestMethod, Description("A failure with no acme error type falls back to a malformed error")]
        public void UntypedExceptionMapsToMalformedResponse()
        {
            var result = AcmeErrorResponseService.CreateAcmeErrorForException(new InvalidOperationException("boom"), "Invalid JWS payload");

            var objectResult = result as Microsoft.AspNetCore.Mvc.ObjectResult;

            Assert.IsNotNull(objectResult);
            Assert.AreEqual(400, objectResult.StatusCode);

            var body = JsonConvert.SerializeObject(objectResult.Value);
            StringAssert.Contains(body, "malformed");
            StringAssert.Contains(body, "Invalid JWS payload");
        }

        #endregion
    }
}
