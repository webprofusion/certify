using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The services a controller needs from its request scope when a test drives it directly.
    /// </summary>
    internal static class TestMvcServices
    {
        /// <summary>
        /// ControllerBase.Problem() resolves a ProblemDetailsFactory from request services, so a controller which
        /// refuses a request needs one registered or it throws instead of returning the refusal.
        /// </summary>
        public static IServiceCollection AddProblemDetailsFactory(this IServiceCollection services)
        {
            return services.AddSingleton<ProblemDetailsFactory, TestProblemDetailsFactory>();
        }

        private sealed class TestProblemDetailsFactory : ProblemDetailsFactory
        {
            public override ProblemDetails CreateProblemDetails(
                HttpContext httpContext,
                int? statusCode = null,
                string title = null,
                string type = null,
                string detail = null,
                string instance = null)
            {
                return new ProblemDetails
                {
                    Status = statusCode ?? StatusCodes.Status500InternalServerError,
                    Title = title,
                    Type = type,
                    Detail = detail,
                    Instance = instance
                };
            }

            public override ValidationProblemDetails CreateValidationProblemDetails(
                HttpContext httpContext,
                ModelStateDictionary modelStateDictionary,
                int? statusCode = null,
                string title = null,
                string type = null,
                string detail = null,
                string instance = null)
            {
                return new ValidationProblemDetails(modelStateDictionary)
                {
                    Status = statusCode ?? StatusCodes.Status400BadRequest,
                    Title = title,
                    Type = type,
                    Detail = detail,
                    Instance = instance
                };
            }
        }
    }
}
