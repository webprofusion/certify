using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Service for creating ACME error responses with appropriate HTTP status codes
    /// </summary>
    public class AcmeErrorResponseService
    {
        /// <summary>
        /// ACME error types according to RFC 8555
        /// </summary>
        public static class AcmeErrorTypes
        {
            public const string Malformed = "urn:ietf:params:acme:error:malformed";
            public const string BadNonce = "urn:ietf:params:acme:error:badNonce";
            public const string ExternalAccountRequired = "urn:ietf:params:acme:error:externalAccountRequired";
            public const string OrderNotFound = "urn:ietf:params:acme:error:orderNotFound";
            public const string OrderNotReady = "urn:ietf:params:acme:error:orderNotReady";
            public const string AuthorizationNotFound = "urn:ietf:params:acme:error:authorizationNotFound";
            public const string Unauthorized = "urn:ietf:params:acme:error:unauthorized";
            public const string RejectedIdentifier = "urn:ietf:params:acme:error:rejectedIdentifier";
            public const string ServerInternal = "urn:ietf:params:acme:error:serverInternal";
        }

        /// <summary>
        /// Creates an appropriate IActionResult for an ACME error
        /// </summary>
        /// <param name="errorType">The ACME error type</param>
        /// <param name="detail">Detailed error message</param>
        /// <returns>IActionResult with appropriate HTTP status code</returns>
        public static IActionResult CreateAcmeError(string errorType, string detail)
        {
            var error = AcmeHelper.CreateAcmeErrorObject(errorType, detail);

            return errorType switch
            {
                AcmeErrorTypes.OrderNotFound or AcmeErrorTypes.AuthorizationNotFound => 
                    new NotFoundObjectResult(error),
                AcmeErrorTypes.Unauthorized => 
                    new ObjectResult(error) { StatusCode = 403 },
                AcmeErrorTypes.ServerInternal => 
                    new ObjectResult(error) { StatusCode = 500 },
                _ => 
                    new BadRequestObjectResult(error)
            };
        }
    }
}
