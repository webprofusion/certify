using System.Linq;
using System.Reflection;
using Certify.Service.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Server.Core.Unit
{
    /// <summary>
    /// Tests that the local service API refuses configuration changes while an instance administered through the hub restricts local changes
    /// </summary>
    [TestClass]
    public class LocalChangeRestrictionEndpointTests
    {
        /// <summary>
        /// Endpoints which are not GETs but are actions or checks rather than configuration changes, or which apply the
        /// restriction themselves
        /// </summary>
        private static readonly string[] NotRestrictedByFilter =
        [
            "AccessController.CheckSecurityPrincipalHasAccess", "AccessController.EvaluateAccessScope", "AccessController.CheckApiTokenHasAccess",
            "AccessController.ResolveApiToken", "AccessController.UnlockAccessToken", "AccessController.Validate",
            "CredentialsController.TestCredentials",
            "ManagedCertificatesController.Search", "ManagedCertificatesController.GetResults", "ManagedCertificatesController.GetSummary",
            "ManagedCertificatesController.TestChallengeResponse", "ManagedCertificatesController.PerformChallengeCleanup",
            "ManagedCertificatesController.PreviewActions", "ManagedCertificatesController.ValidateDeploymentTask",
            "ManagedCertificatesController.BeginAutoRenewal", "ManagedCertificatesController.QueueAllStatusReports",
            "ManagedCertificatesController.GetDeploymentProviderDefinition",
            "ManagedChallengeController.AuthorizeManagedChallengeIdentifiers", "ManagedChallengeController.PerformChallengeResponse",
            "ManagedChallengeController.BeginChallengeResponse", "ManagedChallengeController.CleanupChallengeResponse",
            "SystemController.PerformExport", "SystemController.TestDataStore", "SystemController.CheckDataStoreSchema",
            "SystemController.AttemptDataStoreReconnection", "SystemController.CheckManagementHubCredentials",
            "TagController.PreviewTagScope",

            // apply the restriction themselves
            "ManagedCertificatesController.Update",
            "PreferencesController.SetPreferences",
            "SystemController.JoinManagementHub"
        ];

        [TestMethod, Description("Every local service endpoint which changes configuration is refused while local changes are restricted")]
        public void TestConfigurationEndpointsAreRestricted()
        {
            var controllerType = typeof(ControllerBase);

            var endpoints = controllerType.Assembly.GetTypes()
                .Where(t => controllerType.IsAssignableFrom(t))
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(a => !a.HttpMethods.Contains("GET")))
                    .Select(m => (Name: $"{t.Name}.{m.Name}", IsRestricted: m.GetCustomAttribute<RestrictedLocalChangeAttribute>() != null)))
                .ToList();

            var unknown = NotRestrictedByFilter.Where(n => endpoints.All(e => e.Name != n)).ToList();
            Assert.IsEmpty(unknown, $"Not an endpoint: {string.Join(", ", unknown)}");

            var unrestricted = endpoints.Where(e => !e.IsRestricted).Select(e => e.Name).Except(NotRestrictedByFilter).ToList();
            Assert.IsEmpty(unrestricted, $"Add [RestrictedLocalChange] to these endpoints, or list them in {nameof(NotRestrictedByFilter)} if they do not change configuration: {string.Join(", ", unrestricted)}");
        }
    }
}
