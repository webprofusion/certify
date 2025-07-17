using System;
using System.Collections.Generic;
using System.Linq;
using Certify.Models;
using Certify.Models.Hub;
using SourceGenerator;

namespace Certify.SourceGenerators
{
    // ApiMethods is a static class that provides a central registry of API endpoint definitions.
    //
    // Purpose:
    //   - Used by source generators to automatically create API endpoints, map calls between public and internal APIs, and generate API clients.
    //
    // Key Features:
    //   - Defines HTTP method constants (HttpGet, HttpPost, HttpDelete) for standardized usage.
    //   - Provides GetFormattedTypeName(Type type) to return a formatted string for .NET types, including generics.
    //   - The GetApiDefinitions() method returns a list of GeneratedAPI objects, each describing an API operation:
    //       * OperationName: Name of the API operation.
    //       * OperationMethod: HTTP method (GET, POST, DELETE).
    //       * Comment: Description of the operation.
    //       * PublicAPIController/PublicAPIRoute: Controller and route for the public API.
    //       * ServiceAPIRoute: Route for the internal service API.
    //       * ReturnType: The return type of the operation.
    //       * Params: Dictionary of parameter names and types.
    //       * RequiredPermissions: List of permissions required to access the endpoint.
    //       * Other flags like UseManagementAPI for special routing.
    //
    // Usage:
    //   - This list is consumed by source generators to:
    //       * Generate controller methods for the public API.
    //       * Map public API calls to internal service methods.
    //       * Generate client code for consuming the API.
    //
    // Summary:
    //   ApiMethods enables automated code generation for API endpoints, permissions, and client libraries in the Certify system, reducing manual coding and ensuring consistency across the API surface.
    public class ApiMethods
    {
        public static string HttpGet = "HttpGet";
        public static string HttpPost = "HttpPost";
        public static string HttpDelete = "HttpDelete";

        public static string GetFormattedTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                var genericArguments = type.GetGenericArguments()
                                    .Select(x => x.FullName)
                                    .Aggregate((x1, x2) => $"{x1}, {x2}");
                return $"{type.FullName.Substring(0, type.FullName.IndexOf("`"))}"
                     + $"<{genericArguments}>";
            }

            return type.FullName;
        }

        public static List<GeneratedAPI> GetApiDefinitions()
        {
            // declaring an API definition here is then used by the source generators to:
            // - create the public API endpoint
            // - map the call from the public API to the background service API in the service API Client (interface and implementation)
            // - to then generate the public API clients, run nswag when the public API is running.

            var actionResultTypeName = "Certify.Models.Config.ActionResult";

            return new List<GeneratedAPI>
            {

                new()
                {
                    OperationName = "CheckSecurityPrincipalHasAccess",
                    OperationMethod = HttpPost,
                    Comment = "Check a given security principal has permissions to perform a specific action for a specific resource action",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "securityprincipal/allowedaction",
                    ServiceAPIRoute = "access/securityprincipal/allowedaction",
                    ReturnType = "bool",
                    Params = new Dictionary<string, string> { { "check", nameof(Certify.Models.Hub.AccessCheck) } },
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalCheckAccess)]
                },
                new()
                {
                    OperationName = "GetSecurityPrincipalAssignedRoles",
                    OperationMethod = HttpGet,
                    Comment = "Get list of Assigned Roles for a given security principal",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "securityprincipal/{id}/assignedroles",
                    ServiceAPIRoute = "access/securityprincipal/{id}/assignedroles",
                    ReturnType = $"ICollection<{nameof(AssignedRole)}>",
                    Params = new Dictionary<string, string> { { "id", "string" } },
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalCheckAccess)]
                },
                new()
                {
                    OperationName = "GetSecurityPrincipalRoleStatus",
                    OperationMethod = HttpGet,
                    Comment = "Get list of Assigned Roles etc for a given security principal",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "securityprincipal/{id}/rolestatus",
                    ServiceAPIRoute = "access/securityprincipal/{id}/rolestatus",
                    ReturnType = nameof(RoleStatus),
                    Params = new Dictionary<string, string> { { "id", "string" } },
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalCheckAccess)]
                },
                new()
                {
                    OperationName = "GetAccessRoles",
                    OperationMethod = HttpGet,
                    Comment = "Get list of available security Roles",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "roles",
                    ServiceAPIRoute = "access/roles",
                    ReturnType = $"ICollection<{nameof(Role)}>",
                    RequiredPermissions = [new(ResourceTypes.Role, StandardResourceActions.RoleList)]
                },
                new()
                {
                    OperationName = "GetAssignedAccessTokens",
                    OperationMethod = HttpGet,
                    Comment = "Get list of API assigned access tokens",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "assignedtoken",
                    ServiceAPIRoute = "access/assignedtoken/list",
                    ReturnType = $"ICollection<{nameof(AssignedAccessToken)}>",
                    RequiredPermissions = [new(ResourceTypes.AccessToken, StandardResourceActions.AccessTokenList)]
                },
                new()
                {
                    OperationName = "AddAssignedAccessToken",
                    OperationMethod = HttpPost,
                    Comment = "Add new assigned access token",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "assignedtoken",
                    ServiceAPIRoute = "access/assignedtoken",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "token", "Certify.Models.Hub.AssignedAccessToken" } },
                    RequiredPermissions = [new(ResourceTypes.AccessToken, StandardResourceActions.AccessTokenAdd)]
                },
                new()
                {
                    OperationName = "RemoveAssignedAccessToken",
                    OperationMethod = HttpDelete,
                    Comment = "Remove assigned access token",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "assignedtoken",
                    ServiceAPIRoute = "access/assignedtoken/{id}",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "id", "string" } },
                    RequiredPermissions = [new(ResourceTypes.AccessToken, StandardResourceActions.AccessTokenDelete)]
                },
                new()
                {

                    OperationName = "GetSecurityPrincipals",
                    OperationMethod = HttpGet,
                    Comment = "Get list of available security principals",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "securityprincipals",
                    ServiceAPIRoute = "access/securityprincipals",
                    ReturnType = "ICollection<SecurityPrincipal>",
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalList)]
                },
                new()
                {
                    OperationName = "ValidateSecurityPrincipalPassword",
                    OperationMethod = HttpPost,
                    Comment = "Check password valid for security principal",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "validate",
                    ServiceAPIRoute = "access/validate",
                    ReturnType = "Certify.Models.Hub.SecurityPrincipalCheckResponse",
                    Params = new Dictionary<string, string> { { "passwordCheck", GetFormattedTypeName(typeof(Certify.Models.Hub.SecurityPrincipalPasswordCheck)) } },
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalPasswordValidate)]
                },
                new()
                {

                    OperationName = "UpdateSecurityPrincipalPassword",
                    OperationMethod = HttpPost,
                    Comment = "Update password for security principal",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "updatepassword",
                    ServiceAPIRoute = "access/updatepassword",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "passwordUpdate", "Certify.Models.Hub.SecurityPrincipalPasswordUpdate" } },
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalPasswordUpdate)]
                },
                new()
                {

                    OperationName = "AddSecurityPrincipal",
                    OperationMethod = HttpPost,
                    Comment = "Add new security principal",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "securityprincipal",
                    ServiceAPIRoute = "access/securityprincipal",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "principal", "Certify.Models.Hub.SecurityPrincipal" } },
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalAdd)]
                },
                new()
                {

                    OperationName = "UpdateSecurityPrincipal",
                    OperationMethod = HttpPost,
                    Comment = "Update existing security principal",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "securityprincipal/update",
                    ServiceAPIRoute = "access/securityprincipal/update",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string>
                    {
                        { "principal", "Certify.Models.Hub.SecurityPrincipal" }
                    },
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalUpdate)]
                },
                new()
                {
                    OperationName = "UpdateSecurityPrincipalAssignedRoles",
                    OperationMethod = HttpPost,
                    Comment = "Update assigned roles for a security principal",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "securityprincipal/roles/update",
                    ServiceAPIRoute = "access/securityprincipal/roles/update",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string>
                    {
                        { "update", "Certify.Models.Hub.SecurityPrincipalAssignedRoleUpdate" }
                    },
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalUpdateAssignedRoles)]
                },
                new()
                {
                    OperationName = "RemoveSecurityPrincipal",
                    OperationMethod = HttpDelete,
                    Comment = "Remove security principal",
                    PublicAPIController = "Access",
                    PublicAPIRoute = "securityprincipal",
                    ServiceAPIRoute = "access/securityprincipal/{id}",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "id", "string" } },
                    RequiredPermissions = [new(ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalDelete)]
                },
                new()
                {
                    OperationName = "AddHubManagedInstance",
                    OperationMethod = HttpPost,
                    Comment = "Add new managed instance to the hub",
                    ServiceAPIRoute = "managedinstance",
                    ReturnType = $"Models.Config.ActionResult<{nameof(Models.Hub.ManagedInstanceInfo)}>",
                    Params = new Dictionary<string, string> { { "item", nameof(Models.Hub.ManagedInstanceInfo) } },
                    RequiredPermissions = [new(ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceAdd)]
                },
                new()
                {
                    OperationName = "UpdateHubManagedInstance",
                    OperationMethod = HttpPost,
                    Comment = "Update existing managed instance in the hub",
                    ServiceAPIRoute = "managedinstance/update",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "item", nameof(Models.Hub.ManagedInstanceInfo) } },
                    RequiredPermissions = [new(ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceUpdate)]
                },
                new()
                {
                    OperationName = "RemoveHubManagedInstance",
                    OperationMethod = HttpDelete,
                    Comment = "Remove existing managed instance in the hub",
                    PublicAPIController = "Hub",
                    PublicAPIRoute = "instances/{id}",
                    ServiceAPIRoute = "managedinstance/delete/{id}",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "id", "string" } },
                    RequiredPermissions = [new(ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceDelete)]
                },
                new()
                {
                    OperationName = "GetHubManagedInstance",
                    OperationMethod = HttpGet,
                    Comment = "Get managed instance info",
                    ServiceAPIRoute = "managedinstance/{id}",
                    ReturnType = nameof(Models.Hub.ManagedInstanceInfo),
                    Params = new Dictionary<string, string> { { "id", "string" } },
                    RequiredPermissions = [new(ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList)]
                },
                new()
                {
                    OperationName = "GetHubManagedInstances",
                    OperationMethod = HttpGet,
                    Comment = "Get managed instances",
                    ServiceAPIRoute = "managedinstance/list",
                    ReturnType = $"ICollection<{nameof(Models.Hub.ManagedInstanceInfo)}>",
                    Params = new Dictionary<string, string> { },
                    RequiredPermissions = [new(ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList)]
                },
                new()
                {
                    OperationName = "GetManagedChallenges",
                    OperationMethod = HttpGet,
                    Comment = "Get list of available managed challenges (DNS challenge delegation etc)",
                    PublicAPIController = "ManagedChallenge",
                    PublicAPIRoute = "list",
                    ServiceAPIRoute = "managedchallenge",
                    ReturnType = "ICollection<ManagedChallenge>",
                    RequiredPermissions = [new(ResourceTypes.ManagedChallenge, StandardResourceActions.ManagedChallengeList)]
                },
                new()
                {
                    OperationName = "UpdateManagedChallenge",
                    OperationMethod = HttpPost,
                    Comment = "Add/update a managed challenge (DNS challenge delegation etc)",
                    PublicAPIController = "ManagedChallenge",
                    PublicAPIRoute = "update",
                    ServiceAPIRoute = "managedchallenge",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string>
                    {
                        { "update", "Certify.Models.Hub.ManagedChallenge" }
                    },
                    RequiredPermissions = [new(ResourceTypes.ManagedChallenge, StandardResourceActions.ManagedChallengeUpdate)]
                },
                new()
                {
                    OperationName = "RemoveManagedChallenge",
                    OperationMethod = HttpDelete,
                    Comment = "Delete a managed challenge (DNS challenge delegation etc)",
                    PublicAPIController = "ManagedChallenge",
                    PublicAPIRoute = "remove",
                    ServiceAPIRoute = "managedchallenge/{id}",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string>
                    {
                        { "id", "string" }
                    },
                    RequiredPermissions = [new(ResourceTypes.ManagedChallenge, StandardResourceActions.ManagedChallengeDelete)]
                },
                new()
                {
                    OperationName = "PerformManagedChallenge",
                    OperationMethod = HttpPost,
                    Comment = "Perform a managed challenge (DNS challenge delegation etc)",
                    PublicAPIController = null, // skip public controller implementation
                    ServiceAPIRoute = "managedchallenge/request",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string>
                    {
                        { "request", "Certify.Models.Hub.ManagedChallengeRequest" }
                    },
                    RequiredPermissions = [new(ResourceTypes.ManagedChallenge, StandardResourceActions.ManagedChallengeRequest)]
                },
                new()
                {
                    OperationName = "CleanupManagedChallenge",
                    OperationMethod = HttpPost,
                    Comment = "Perform cleanup for a previously managed challenge (DNS challenge delegation etc)",
                    PublicAPIController = null, // skip public controller implementation
                    ServiceAPIRoute = "managedchallenge/cleanup",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string>
                    {
                        { "request", GetFormattedTypeName(typeof(Certify.Models.Hub.ManagedChallengeRequest)) }
                    },
                    RequiredPermissions = [new(ResourceTypes.ManagedChallenge, StandardResourceActions.ManagedChallengeCleanup)]
                },
                new()
                {
                    OperationName = "PerformExport",
                    OperationMethod = HttpPost,
                    Comment = "Perform an export of all settings for an instance",
                    ServiceAPIRoute = "system/migration/export",
                    ReturnType = GetFormattedTypeName(typeof(Models.Config.Migration.ImportExportPackage)),
                    Params = new Dictionary<string, string> { { "exportRequest", GetFormattedTypeName(typeof(Certify.Models.Config.Migration.ExportRequest)) } },
                    RequiredPermissions = [new(ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceExport)]
                },
                new()
                {
                    OperationName = "PerformImport",
                    OperationMethod = HttpPost,
                    Comment = "Perform an import of all settings for an instance",
                    ServiceAPIRoute = "system/migration/import",
                    ReturnType = "ICollection<ActionStep>",
                    Params = new Dictionary<string, string> { { "importRequest", GetFormattedTypeName(typeof(Certify.Models.Config.Migration.ImportRequest)) } },
                    RequiredPermissions = [new(ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceImport)]
                },
                /* per instance API, via management hub */
                new()
                {
                    OperationName = "GetAcmeAccounts",
                    OperationMethod = HttpGet,
                    Comment = "Get All Acme Accounts",
                    UseManagementAPI = true,
                    PublicAPIController = "CertificateAuthority",
                    PublicAPIRoute = "{instanceId}/accounts/",
                    ReturnType = "ICollection<Certify.Models.AccountDetails>",
                    Params = new Dictionary<string, string> { { "instanceId", "string" } },
                    RequiredPermissions = [new(ResourceTypes.AcmeAccount, StandardResourceActions.AcmeAccountList)]
                },
                new()
                {
                    OperationName = "AddAcmeAccount",
                    OperationMethod = HttpPost,
                    Comment = "Add New Acme Account",
                    UseManagementAPI = true,
                    PublicAPIController = "CertificateAuthority",
                    PublicAPIRoute = "{instanceId}/account/",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "registration", "Certify.Models.ContactRegistration" } },
                    RequiredPermissions = [new(ResourceTypes.AcmeAccount, StandardResourceActions.AcmeAccountAdd)]
                },
                new()
                {
                    OperationName = "GetCertificateAuthorities",
                    OperationMethod = HttpGet,
                    Comment = "Get list of defined Certificate Authorities",
                    UseManagementAPI = true,
                    PublicAPIController = "CertificateAuthority",
                    PublicAPIRoute = "{instanceId}/authority",
                    ReturnType = "ICollection<Certify.Models.CertificateAuthority>",
                    Params = new Dictionary<string, string> { { "instanceId", "string" } },
                    RequiredPermissions = [new(ResourceTypes.CertificateAuthority, StandardResourceActions.CertificateAuthorityList)]
                },
                new()
                {
                    OperationName = "UpdateCertificateAuthority",
                    OperationMethod = HttpPost,
                    Comment = "Add/Update Certificate Authority",
                    UseManagementAPI = true,
                    PublicAPIController = "CertificateAuthority",
                    PublicAPIRoute = "{instanceId}/authority",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "ca", "Certify.Models.CertificateAuthority" } },
                    RequiredPermissions = [new(ResourceTypes.CertificateAuthority, StandardResourceActions.CertificateAuthorityUpdate)]
                },
                new()
                {
                    OperationName = "RemoveCertificateAuthority",
                    OperationMethod = HttpDelete,
                    Comment = "Remove Certificate Authority",
                    UseManagementAPI = true,
                    PublicAPIController = "CertificateAuthority",
                    PublicAPIRoute = "{instanceId}/authority/{id}",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "id", "string" } },
                    RequiredPermissions = [new(ResourceTypes.CertificateAuthority, StandardResourceActions.CertificateAuthorityDelete)]
                },
                new()
                {
                    OperationName = "RemoveAcmeAccount",
                    OperationMethod = HttpDelete,
                    Comment = "Remove ACME Account",
                    UseManagementAPI = true,
                    PublicAPIController = "CertificateAuthority",
                    PublicAPIRoute = "{instanceId}/accounts/{storageKey}/{deactivate}",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "storageKey", "string" }, { "deactivate", "bool" } },
                    RequiredPermissions = [new(ResourceTypes.AcmeAccount, StandardResourceActions.AcmeAccountDelete)]
                },
                new()
                {
                    OperationName = "GetStoredCredentials",
                    OperationMethod = HttpGet,
                    Comment = "Get List of Stored Credentials",
                    UseManagementAPI = true,
                    PublicAPIController = "StoredCredential",
                    PublicAPIRoute = "{instanceId}",
                    ReturnType = "ICollection<Certify.Models.Config.StoredCredential>",
                    Params = new Dictionary<string, string> { { "instanceId", "string" } },
                    RequiredPermissions = [new(ResourceTypes.StoredCredential, StandardResourceActions.StoredCredentialList)]
                },
                new()
                {
                    OperationName = "UpdateStoredCredential",
                    OperationMethod = HttpPost,
                    Comment = "Add/Update Stored Credential",
                    PublicAPIController = "StoredCredential",
                    PublicAPIRoute = "{instanceId}",
                    ReturnType = actionResultTypeName,
                    UseManagementAPI = true,
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "item", GetFormattedTypeName(typeof(Certify.Models.Config.StoredCredential)) } },
                    RequiredPermissions = [new(ResourceTypes.StoredCredential, StandardResourceActions.StoredCredentialUpdate)]
                },
                new()
                {
                    OperationName = "RemoveStoredCredential",
                    OperationMethod = HttpDelete,
                    Comment = "Remove Stored Credential",
                    UseManagementAPI = true,
                    PublicAPIController = "StoredCredential",
                    PublicAPIRoute = "{instanceId}/{storageKey}",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "storageKey", "string" } },
                    RequiredPermissions = [new(ResourceTypes.StoredCredential, StandardResourceActions.StoredCredentialDelete)]
                },
                new()
                {
                    OperationName = "UnlockStoredCredential",
                    OperationMethod = HttpPost,
                    Comment = "Unlock Stored Credential",
                    UseManagementAPI = true,
                    PublicAPIController = "StoredCredential",
                    PublicAPIRoute = "{instanceId}/{storageKey}/unlock",
                    ReturnType = GetFormattedTypeName(typeof(Models.Config.StoredCredentialUnlockResult)),
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "storageKey", "string" } },
                    RequiredPermissions = [new(ResourceTypes.StoredCredential, StandardResourceActions.StoredCredentialReadSecret)]
                },
                new()
                {
                    OperationName = "GetDeploymentProviders",
                    OperationMethod = HttpGet,
                    Comment = "Get Deployment Task Providers",
                    UseManagementAPI = true,
                    PublicAPIController = "DeploymentTask",
                    PublicAPIRoute = "{instanceId}",
                    ReturnType = "ICollection<Certify.Models.Config.DeploymentProviderDefinition>",
                    Params = new Dictionary<string, string>
                    {
                        { "instanceId", "string" }
                    },
                    RequiredPermissions = [new(ResourceTypes.DeploymentTask, StandardResourceActions.DeploymentTaskListProviders)]
                },
                new()
                 {
                     OperationName = "GetTargetIPAddresses",
                     OperationMethod = HttpGet,
                     Comment = "Get list of IP addresses available on the target for service binding (IIS, nginx etc)",
                     UseManagementAPI = true,
                     ManagementHubCommandType = Models.Hub.ManagementHubCommands.GetTargetIPAddresses,
                     PublicAPIController = "Target",
                     PublicAPIRoute = "{instanceId}/ipaddresses",
                     ReturnType = "ICollection<IPAddressOption>",
                     Params = new Dictionary<string, string>
                     {
                         { "instanceId", "string" }
                     },
                     RequiredPermissions = [new(ResourceTypes.Target, StandardResourceActions.TargetIPAddressesList)]
                 },
                new()
                {
                    OperationName = "GetTargetServiceTypes",
                    OperationMethod = HttpGet,
                    Comment = "Get Service Types present on instance (IIS, nginx etc)",
                    UseManagementAPI = true,
                    ManagementHubCommandType = Models.Hub.ManagementHubCommands.GetTargetServiceTypes,
                    PublicAPIController = "Target",
                    PublicAPIRoute = "{instanceId}/types",
                    ReturnType = "ICollection<string>",
                    Params = new Dictionary<string, string>
                    {
                        { "instanceId", "string" }
                    },
                    RequiredPermissions = [new(ResourceTypes.Target, StandardResourceActions.TargetTypesList)]
                },
                new()
                {
                    OperationName = "GetTargetServiceItems",
                    OperationMethod = HttpGet,
                    Comment = "Get Service items (sites) present on instance (IIS, nginx etc).",
                    UseManagementAPI = true,
                    ManagementHubCommandType = Models.Hub.ManagementHubCommands.GetTargetServiceItems,
                    PublicAPIController = "Target",
                    PublicAPIRoute = "{instanceId}/{serviceType}/items",
                    ReturnType = "ICollection<SiteInfo>",
                    Params = new Dictionary<string, string>
                    {
                        { "instanceId", "string" },
                        { "serviceType", "string" }
                    },
                    RequiredPermissions = [new(ResourceTypes.Target, StandardResourceActions.TargetServiceItemsList)]
                },
                new()
                {
                    OperationName = "GetTargetServiceItemIdentifiers",
                    OperationMethod = HttpGet,
                    Comment = "Get Service item identifiers (domains on a website etc) present on instance (IIS, nginx etc)",
                    UseManagementAPI = true,
                    ManagementHubCommandType = Models.Hub.ManagementHubCommands.GetTargetServiceItemIdentifiers,
                    PublicAPIController = "Target",
                    PublicAPIRoute = "{instanceId}/{serviceType}/item/{itemId}/identifiers",
                    ReturnType = "ICollection<DomainOption>",
                    Params = new Dictionary<string, string>
                    {
                        { "instanceId", "string" },
                        { "serviceType", "string" },
                        { "itemId", "string" }
                    },
                    RequiredPermissions = [new(ResourceTypes.Target, StandardResourceActions.TargetServiceItemIdentifiersList)]
                },
                new()
                {
                    OperationName = "GetChallengeProviders",
                    OperationMethod = HttpGet,
                    Comment = "Get Dns Challenge Providers",
                    UseManagementAPI = true,
                    PublicAPIController = "ChallengeProvider",
                    PublicAPIRoute = "{instanceId}",
                    ReturnType = "ICollection<Certify.Models.Config.ChallengeProviderDefinition>",
                    Params = new Dictionary<string, string>
                    {
                        { "instanceId", "string" }
                    },
                    RequiredPermissions = [new(ResourceTypes.ChallengeProvider, StandardResourceActions.ChallengeProviderList)]
                },
                new()
                {
                    OperationName = "GetDnsZones",
                    OperationMethod = HttpGet,
                    Comment = "Get List of Zones with the current DNS provider and credential",
                    UseManagementAPI = true,
                    PublicAPIController = "ChallengeProvider",
                    PublicAPIRoute = "{instanceId}/dnszones/{providerTypeId}/{credentialId}",
                    ReturnType = "Certify.Models.Providers.DnsZoneQueryResult",
                    Params = new Dictionary<string, string>
                    {
                        { "instanceId", "string" },
                        { "providerTypeId", "string" },
                        { "credentialId", "string" }
                    },
                    RequiredPermissions = [new(ResourceTypes.ChallengeProvider, StandardResourceActions.ChallengeProviderDnsZonesList)]
                },
                new()
                {
                    OperationName = "ExecuteDeploymentTask",
                    OperationMethod = HttpGet,
                    Comment = "Execute Deployment Task",
                    UseManagementAPI = true,
                    PublicAPIController = "DeploymentTask",
                    PublicAPIRoute = "{instanceId}/execute/{managedCertificateId}/{taskId}",
                    ReturnType = "ICollection<ActionStep>",
                    Params = new Dictionary<string, string>
                    {
                        { "instanceId", "string" },
                        { "managedCertificateId", "string" },
                        { "taskId", "string" }
                    },
                    RequiredPermissions = [new(ResourceTypes.DeploymentTask, StandardResourceActions.DeploymentTaskExecute)]
                },
                new()
                {
                    OperationName = "RemoveManagedCertificate",
                    OperationMethod = HttpDelete,
                    Comment = "Remove Managed Certificate",
                    UseManagementAPI = true,
                    PublicAPIController = "Certificate",
                    PublicAPIRoute = "{instanceId}/settings/{managedCertId}",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "managedCertId", "string" } },
                    RequiredPermissions = [new(ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemDelete)]
                },
                new()
                {
                    OperationName = "PerformInstanceExport",
                    OperationMethod = HttpPost,
                    Comment = "Perform an export of all settings",
                    UseManagementAPI = true,
                    PublicAPIController = "System",
                    PublicAPIRoute = "{instanceId}/system/migration/export",
                    ReturnType =  GetFormattedTypeName(typeof(Models.Config.Migration.ImportExportPackage)),
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "exportRequest",  GetFormattedTypeName(typeof(Certify.Models.Config.Migration.ExportRequest)) } },
                    RequiredPermissions = [new(ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceExport)]
                },
                new()
                {
                    OperationName = "PerformInstanceImport",
                    OperationMethod = HttpPost,
                    Comment = "Perform an import of all settings",
                    UseManagementAPI = true,
                    PublicAPIController = "System",
                    PublicAPIRoute = "{instanceId}/system/migration/import",
                    ReturnType = "ICollection<ActionStep>",
                    Params = new Dictionary<string, string> { { "instanceId", "string" }, { "importRequest",  GetFormattedTypeName(typeof(Certify.Models.Config.Migration.ImportRequest)) } },
                    RequiredPermissions = [new(ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceImport)]
                },
                new()
                {
                    OperationName = "GetInstanceStatusItems",
                    OperationMethod = HttpGet,
                    Comment = "Get instance status items",
                    UseManagementAPI = true,
                    PublicAPIController = "System",
                    PublicAPIRoute = "{instanceId}/system/status",
                    ReturnType = "ICollection<ActionStep>",
                    Params = new Dictionary<string, string> { { "instanceId", "string" } },
                    RequiredPermissions = [new(ResourceTypes.System, StandardResourceActions.SystemStatusList)]
                },
                new()
                {
                    OperationName = "GetServiceConfig",
                    OperationMethod = HttpGet,
                    Comment = "Get service config for a managed instance",
                    UseManagementAPI = true,
                    PublicAPIController = "System",
                    PublicAPIRoute = "{instanceId}/system/serviceconfig",
                    ReturnType = "Certify.Shared.ServiceConfig",
                    Params = new Dictionary<string, string> { { "instanceId", "string" } },
                    RequiredPermissions = [new(ResourceTypes.System, StandardResourceActions.SystemServiceConfigList)]
                },
                new()
                {
                    OperationName = "GetServiceCoreSettings",
                    OperationMethod = HttpGet,
                    Comment = "Get core settings for a managed instance",
                    UseManagementAPI = true,
                    PublicAPIController = "System",
                    PublicAPIRoute = "{instanceId}/system/coresettings",
                    ReturnType = nameof(Preferences),
                    Params = new Dictionary<string, string> { { "instanceId", "string" } },
                    RequiredPermissions = [new(ResourceTypes.System, StandardResourceActions.SystemCoreSettingsList)]
                },
                new()
                {
                    OperationName = "UpdateServiceConfig",
                    OperationMethod = HttpPost,
                    Comment = "Update instance service config",
                    UseManagementAPI = true,
                    PublicAPIController = "System",
                    PublicAPIRoute = "{instanceId}/system/serviceconfig",
                    ReturnType = actionResultTypeName,
                    Params = new Dictionary<string, string>
                    {
                        { "instanceId", "string" },
                        { "config", "Certify.Shared.ServiceConfig" }
                    },
                    RequiredPermissions = [new(ResourceTypes.System, StandardResourceActions.SystemServiceConfigUpdate)]
                },
                 new()
                 {
                     OperationName = "UpdateServiceCoreSettings",
                     OperationMethod = HttpPost,
                     Comment = "Update instance service core settings",
                     UseManagementAPI = true,
                     PublicAPIController = "System",
                     PublicAPIRoute = "{instanceId}/system/coresettings",
                     ReturnType = actionResultTypeName,
                     Params = new Dictionary<string, string>
                     {
                         { "instanceId", "string" },
                         { "prefs", "Certify.Models.Preferences" }
                     },
                     RequiredPermissions = [new(ResourceTypes.System, StandardResourceActions.SystemCoreSettingsUpdate)]
                 },
                 new()
                    {
                        OperationName = "GetHubItemTags",
                        OperationMethod = HttpGet,
                        Comment = "Get hub item tags",
                        PublicAPIController = "Hub",
                        PublicAPIRoute = "tags/list",
                        ServiceAPIRoute = "tags/list",
                        ReturnType = "ICollection<ItemTag>",
                        RequiredPermissions = [new(ResourceTypes.Tag, StandardResourceActions.TagList)]
                    },
                   new()
                  {
                      OperationName = "AddHubItemTag",
                      OperationMethod = HttpPost,
                      Comment = "Add hub item tag",
                      PublicAPIController = "Hub",
                      PublicAPIRoute = "tags/add",
                      ServiceAPIRoute = "tags/add",
                      ReturnType = actionResultTypeName,
                      Params = new Dictionary<string, string> { { "tag", GetFormattedTypeName(typeof(ItemTag)) } },
                      RequiredPermissions = [new(ResourceTypes.Tag, StandardResourceActions.TagAdd)]
                  },
            };
        }
    }
}
