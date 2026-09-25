using Certify.Client;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Per-resource scope checks. An endpoint first checks that the caller holds its action at all; these then narrow
    /// that to the resource the caller named, applying the tag scopes, domain restrictions and included resources on the
    /// role assignments which grant the action (see <see cref="ResourceScope"/>). A resource outside that scope is
    /// reported as not found, as the listings do not show it either.
    /// </summary>
    public partial class ApiControllerBase
    {
        #region Identifiers

        /// <summary>
        /// Check that a set of identifiers, all carried by one resource such as a new managed item, is within the
        /// domain restrictions on the caller's role assignments for a resource action. The caller gains access to all of
        /// them together, so a single assignment must permit every one.
        /// </summary>
        internal async Task<Certify.Models.Config.ActionResult> CheckIdentifiersAuthorized(
            ICertifyInternalApiClient internalApiClient,
            string resourceActionId,
            IEnumerable<string?>? identifiers)
        {
            // the caller may have authorized by bearer token or by API access token, and an access token request
            // reaching an [AllowAnonymous] endpoint has no authenticated HttpContext.User to read the principal from
            var authContext = RequestAuthContext;

            if (string.IsNullOrWhiteSpace(authContext?.UserId))
            {
                return new Certify.Models.Config.ActionResult("No authenticated security principal to check domain restrictions for", false);
            }

            var scope = await ResourceScope.Resolve(internalApiClient, authContext, ResourceTypes.Domain, resourceActionId);

            if (!scope.HasAction)
            {
                // fail closed, a transient evaluation error must not promote a restricted principal to unrestricted
                return new Certify.Models.Config.ActionResult("Could not evaluate access scope for domain restrictions", false);
            }

            if (!scope.RequiresIdentifiers)
            {
                return new Certify.Models.Config.ActionResult("No domain restrictions apply", true);
            }

            var identifierList = identifiers?
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            if (identifierList.Count == 0)
            {
                return new Certify.Models.Config.ActionResult("No identifiers to check against the domain restrictions on this role assignment", false);
            }

            var denied = identifierList.FirstOrDefault(i => !scope.PermitsIdentifiers([i]));

            if (denied != null)
            {
                return new Certify.Models.Config.ActionResult($"Identifier '{denied}' is not permitted by the domain restrictions on this role assignment", false);
            }

            return scope.PermitsIdentifiers(identifierList)
                ? new Certify.Models.Config.ActionResult("Authorized for all identifiers", true)
                : new Certify.Models.Config.ActionResult("The identifiers are permitted by different role assignments, and no single assignment permits all of them", false);
        }

        #endregion

        #region Managed items

        /// <summary>
        /// Check that a managed item is within the caller's scope for a managed item action. The endpoint checks the
        /// action itself first; this narrows it to the one item, which the caller may have named by an id the listing
        /// never showed them.
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="mgmtAPI"></param>
        /// <param name="resourceActionId">the managed item action the endpoint performs</param>
        /// <param name="instanceId">instance holding the item</param>
        /// <param name="managedCertId">the item</param>
        /// <param name="item">the item where the endpoint has already fetched it, otherwise it is looked up</param>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckManagedItemInScope(
            ICertifyInternalApiClient internalApiClient,
            ManagementAPI mgmtAPI,
            string resourceActionId,
            string? instanceId,
            string? managedCertId,
            ManagedCertificate? item = null)
        {
            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, ResourceTypes.ManagedItem, resourceActionId);

            if (!scope.HasAction)
            {
                return ScopeNotEvaluated(resourceActionId);
            }

            if (scope.IsUnrestricted)
            {
                return null;
            }

            item ??= await FindManagedItem(mgmtAPI, instanceId, managedCertId);

            return item != null && await IsManagedItemInScope(internalApiClient, scope, instanceId, item)
                ? null
                : NotFoundInScope("Managed item not found");
        }

        /// <summary>
        /// Check a managed item configuration the caller has submitted, to save, test or preview, against their scope
        /// for a managed item action.
        ///
        /// Where it is an existing item, that item must be within scope as it stands. Where it is a new item, the
        /// instance it is for must be within scope, and its id must not already belong to an item on another instance,
        /// which would otherwise let the new item be read as that item. Every identifier the configuration names must be
        /// within the domain restrictions for the action. The configuration's tags are not considered, as tags are held
        /// by the hub and a new item is only tagged once it has been saved.
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="mgmtAPI"></param>
        /// <param name="resourceActionId">the managed item action the endpoint performs</param>
        /// <param name="instanceId">instance the configuration is for</param>
        /// <param name="submittedItem">the configuration as the caller submitted it</param>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckSubmittedManagedItemInScope(
            ICertifyInternalApiClient internalApiClient,
            ManagementAPI mgmtAPI,
            string resourceActionId,
            string? instanceId,
            ManagedCertificate? submittedItem)
        {
            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, ResourceTypes.ManagedItem, resourceActionId);

            if (!scope.HasAction)
            {
                return ScopeNotEvaluated(resourceActionId);
            }

            // the instance is only asked for the item when the id is already in use elsewhere, so an unrestricted
            // caller saving a new item does not wait on an extra round trip
            if (IsManagedItemIdInUseElsewhere(mgmtAPI, instanceId, submittedItem?.Id)
                && await FindManagedItem(mgmtAPI, instanceId, submittedItem?.Id) == null)
            {
                return Problem(
                    detail: "A managed item with this id already exists on another instance. Save it without an id to create a new item.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (scope.IsUnrestricted)
            {
                return null;
            }

            var existingItem = await FindManagedItem(mgmtAPI, instanceId, submittedItem?.Id);

            if (existingItem != null)
            {
                if (!await IsManagedItemInScope(internalApiClient, scope, instanceId, existingItem))
                {
                    return NotFoundInScope("Managed item not found");
                }
            }
            else if (!(await GetInstancesInScope(internalApiClient, mgmtAPI, scope, [instanceId])).Any())
            {
                return NotFoundInScope("Managed instance not found");
            }

            if (!scope.PermitsIdentifiers(submittedItem?.GetCertificateIdentifiers().Select(i => i.Value)))
            {
                return Problem(
                    detail: "The managed item's identifiers are not all permitted by the domain restrictions on this role assignment",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            return null;
        }

        /// <summary>
        /// The hub's cached copy of an item is what the item listing shows, so that is what is checked; an item not
        /// cached yet is asked for from its instance.
        /// </summary>
        private async Task<ManagedCertificate?> FindManagedItem(ManagementAPI mgmtAPI, string? instanceId, string? managedCertId)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(managedCertId))
            {
                return null;
            }

            return mgmtAPI.GetCachedManagedCertificate(instanceId, managedCertId)
                ?? await mgmtAPI.GetManagedCertificate(instanceId, managedCertId, CurrentAuthContext);
        }

        private static async Task<bool> IsManagedItemInScope(ICertifyInternalApiClient internalApiClient, ResourceScope scope, string? instanceId, ManagedCertificate item)
        {
            // an item whose tags cannot be found is untagged, which no tag scoped caller reaches
            var tags = scope.RequiresTags
                ? await HubItemTags.GetItemTags(internalApiClient, TaggedItemTypes.ManagedCertificate, instanceId ?? item.InstanceId, item.Id)
                : null;

            return scope.Permits(tags, item.GetCertificateIdentifiers().Select(i => i.Value));
        }

        /// <summary>
        /// The access check for downloading a certificate: its tags and every identifier on it, which the access control
        /// answers per role assignment so that one assignment must permit both. The whole cert is downloaded, so every
        /// identifier on it counts.
        /// </summary>
        /// <param name="securityPrincipalId">the principal downloading, or null for the caller</param>
        /// <param name="cert">the managed item holding the certificate</param>
        /// <param name="tags">the item's tags on its instance</param>
        /// <param name="scopedAssignedRoles">the role assignments an API access token narrows the principal to, if any</param>
        internal static AccessCheck CertificateDownloadCheck(string? securityPrincipalId, ManagedCertificate cert, IEnumerable<ITaggedValue>? tags, ICollection<string>? scopedAssignedRoles)
        {
            return new AccessCheck
            {
                SecurityPrincipalId = securityPrincipalId,
                ResourceType = ResourceTypes.Certificate,
                ResourceActionId = StandardResourceActions.CertificateDownload,
                Identifier = cert.Id,
                ResourceTags = tags?.Select(t => new TagSummary { CategoryKey = t.CategoryKey, Value = t.Value }).ToList() ?? [],
                ResourceIdentifiers = cert.GetCertificateIdentifiers().Select(i => i.Value).ToList(),
                ScopedAssignedRoles = scopedAssignedRoles?.ToList() ?? []
            };
        }

        private static bool IsManagedItemIdInUseElsewhere(ManagementAPI mgmtAPI, string? instanceId, string? managedCertId)
        {
            if (string.IsNullOrWhiteSpace(managedCertId))
            {
                return false;
            }

            return mgmtAPI.GetManagedInstanceItems().Values.Any(instance =>
                !string.Equals(instance.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)
                && instance.Items?.Any(i => string.Equals(i.Id, managedCertId, StringComparison.OrdinalIgnoreCase)) == true);
        }

        #endregion

        #region Managed instances

        /// <summary>
        /// Check that a managed instance is within the caller's scope for a resource action performed on it. An instance
        /// is within scope when it carries the tags of a tag scoped assignment granting the action, or holds a managed
        /// item that assignment permits, so a scoped caller reaches the instances their items are on.
        /// </summary>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckInstanceInScope(
            ICertifyInternalApiClient internalApiClient,
            ManagementAPI mgmtAPI,
            string resourceType,
            string resourceActionId,
            string? instanceId)
        {
            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, resourceType, resourceActionId);

            if (!scope.HasAction)
            {
                return ScopeNotEvaluated(resourceActionId);
            }

            if (scope.IsUnrestricted)
            {
                return null;
            }

            return (await GetInstancesInScope(internalApiClient, mgmtAPI, scope, [instanceId])).Any()
                ? null
                : NotFoundInScope("Managed instance not found");
        }

        /// <summary>
        /// The instances, of those given, within a resolved scope
        /// </summary>
        internal static Task<HashSet<string>> GetInstancesInScope(
            ICertifyInternalApiClient internalApiClient,
            ManagementAPI mgmtAPI,
            ResourceScope scope,
            IEnumerable<string?> instanceIds)
            => scope.GetInstancesInScope(internalApiClient, mgmtAPI.GetManagedInstanceItems().Values, instanceIds);

        #endregion

        #region Stored credentials

        /// <summary>
        /// Check that a stored credential is within the caller's scope for a stored credential action, from the
        /// credential's tags and, for an assignment restricted to specific credentials, its storage key. Where a new
        /// credential may be created, one which does not exist yet needs its instance to be within scope instead.
        /// </summary>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckStoredCredentialInScope(
            ICertifyInternalApiClient internalApiClient,
            ManagementAPI mgmtAPI,
            string resourceActionId,
            string? instanceId,
            string? storageKey,
            bool allowNew)
        {
            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, ResourceTypes.StoredCredential, resourceActionId);

            if (!scope.HasAction)
            {
                return ScopeNotEvaluated(resourceActionId);
            }

            if (scope.IsUnrestricted)
            {
                return null;
            }

            if (allowNew)
            {
                var exists = !string.IsNullOrWhiteSpace(storageKey)
                    && (await mgmtAPI.GetStoredCredentials(instanceId!, CurrentAuthContext))?.Any(c => c.StorageKey == storageKey) == true;

                if (!exists)
                {
                    return (await GetInstancesInScope(internalApiClient, mgmtAPI, scope, [instanceId])).Any()
                        ? null
                        : NotFoundInScope("Managed instance not found");
                }
            }

            var tags = await HubItemTags.GetItemTags(internalApiClient, TaggedItemTypes.StoredCredential, instanceId, storageKey);

            return scope.PermitsTags(tags, storageKey) ? null : NotFoundInScope("Stored credential not found");
        }

        /// <summary>
        /// Narrow a stored credential listing to the credentials within the caller's scope
        /// </summary>
        internal async Task<ICollection<StoredCredential>?> FilterStoredCredentialsInScope(
            ICertifyInternalApiClient internalApiClient,
            string resourceActionId,
            string? instanceId,
            ICollection<StoredCredential>? credentials)
        {
            if (credentials == null)
            {
                return null;
            }

            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, ResourceTypes.StoredCredential, resourceActionId);

            if (scope.IsUnrestricted)
            {
                return credentials;
            }

            var tags = scope.HasAction ? await HubItemTags.GetAllItemTags(internalApiClient, TaggedItemTypes.StoredCredential) : null;

            return credentials
                .Where(c => tags != null && scope.PermitsTags(HubItemTags.ForItem(tags, instanceId, c.StorageKey), c.StorageKey))
                .ToList();
        }

        #endregion

        #region Managed challenges

        /// <summary>
        /// Check that a managed challenge is within the caller's scope for a managed challenge action. Where a new
        /// challenge may be created, one which does not exist yet is permitted, as it has no tags until it is saved.
        /// </summary>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckManagedChallengeInScope(
            ICertifyInternalApiClient internalApiClient,
            string resourceActionId,
            string? managedChallengeId,
            bool allowNew)
        {
            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, ResourceTypes.ManagedChallenge, resourceActionId);

            if (!scope.HasAction)
            {
                return ScopeNotEvaluated(resourceActionId);
            }

            if (scope.IsUnrestricted)
            {
                return null;
            }

            if (allowNew)
            {
                var exists = !string.IsNullOrWhiteSpace(managedChallengeId)
                    && (await internalApiClient.GetManagedChallenges(SystemAuthContext))?.Any(c => c.Id == managedChallengeId) == true;

                if (!exists)
                {
                    return null;
                }
            }

            var tags = await HubItemTags.GetItemTags(internalApiClient, TaggedItemTypes.ManagedChallenge, instanceId: null, managedChallengeId);

            return scope.PermitsTags(tags, managedChallengeId) ? null : NotFoundInScope("Managed challenge not found");
        }

        /// <summary>
        /// Narrow a managed challenge listing to the challenges within the caller's scope. A tag scoped caller never
        /// sees an untagged challenge.
        /// </summary>
        internal async Task<ICollection<ManagedChallenge>> FilterManagedChallengesInScope(
            ICertifyInternalApiClient internalApiClient,
            string resourceActionId,
            ICollection<ManagedChallenge>? challenges)
        {
            if (challenges == null)
            {
                return [];
            }

            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, ResourceTypes.ManagedChallenge, resourceActionId);

            if (scope.IsUnrestricted)
            {
                return challenges;
            }

            var tags = scope.HasAction ? await HubItemTags.GetAllItemTags(internalApiClient, TaggedItemTypes.ManagedChallenge) : null;

            return challenges
                .Where(c => tags != null && scope.PermitsTags(HubItemTags.ForItem(tags, instanceId: null, c.Id), c.Id))
                .ToList();
        }

        #endregion

        #region Tags

        /// <summary>
        /// Narrow a set of item tags to those on items within the caller's scope for listing tags, so that a tag scoped
        /// caller does not learn the ids, instances or tags of items outside their scope
        /// </summary>
        internal async Task<ICollection<ItemTag>> FilterItemTagsInScope(ICertifyInternalApiClient internalApiClient, ICollection<ItemTag>? tags)
        {
            if (tags == null)
            {
                return [];
            }

            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, ResourceTypes.Tag, StandardResourceActions.TagList);

            if (scope.IsUnrestricted)
            {
                return tags;
            }

            return tags
                .GroupBy(t => (t.TaggedItemType, t.TaggedItemId, t.InstanceId))
                .Where(item => scope.PermitsTags(item))
                .SelectMany(item => item)
                .ToList();
        }

        /// <summary>
        /// The tags of one item, or none when the item is outside the caller's scope for listing tags
        /// </summary>
        internal async Task<ICollection<TagSummary>> FilterItemTagSummariesInScope(ICertifyInternalApiClient internalApiClient, ICollection<TagSummary>? tags)
        {
            if (tags == null)
            {
                return [];
            }

            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, ResourceTypes.Tag, StandardResourceActions.TagList);

            return scope.IsUnrestricted || scope.PermitsTags(tags) ? tags : [];
        }

        /// <summary>
        /// Previewing what a tag scope matches counts items across the whole hub, so it is only answered for a caller
        /// whose own tag listing is unrestricted
        /// </summary>
        internal async Task<ScopePreviewResult> LimitScopePreviewToCaller(ICertifyInternalApiClient internalApiClient, ScopePreviewResult? preview)
        {
            var scope = await ResourceScope.Resolve(internalApiClient, CurrentAuthContext, ResourceTypes.Tag, StandardResourceActions.TagList);

            return scope.IsUnrestricted && preview != null
                ? preview
                : new ScopePreviewResult { ScopeDescription = preview?.ScopeDescription ?? string.Empty };
        }

        #endregion

        #region Program execution

        /// <summary>
        /// Adding or changing settings which run a program or script on an instance (see
        /// <see cref="ProgramExecutionSettings"/>) is limited to administrators, whatever else the caller may do with the
        /// resource, as those settings choose code the instance runs as its service account.
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="addsOrChanges">whether the submission adds or changes any such setting</param>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckProgramExecutionSettingsAllowed(ICertifyInternalApiClient internalApiClient, bool addsOrChanges)
        {
            if (!addsOrChanges || await PrincipalAccess.IsAdministrator(internalApiClient, CurrentAuthContext))
            {
                return null;
            }

            return Problem(
                detail: "Only an administrator can add or change deployment tasks, scripts or DNS providers which run a program or script.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        /// <summary>
        /// Check the program execution settings of a managed item the caller has submitted to save or test, against the
        /// item as its instance holds it. Settings the item already has, submitted unchanged, are allowed.
        /// </summary>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckSubmittedManagedItemProgramExecution(
            ICertifyInternalApiClient internalApiClient,
            ManagementAPI mgmtAPI,
            string? instanceId,
            ManagedCertificate? submittedItem)
        {
            // the instance is only asked for its copy when the submitted item carries such settings at all
            if (!ProgramExecutionSettings.AddsOrChanges(existing: null, submittedItem))
            {
                return null;
            }

            var existingItem = string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(submittedItem?.Id)
                ? null
                : await mgmtAPI.GetManagedCertificate(instanceId, submittedItem.Id, CurrentAuthContext);

            return await CheckProgramExecutionSettingsAllowed(internalApiClient, ProgramExecutionSettings.AddsOrChanges(existingItem, submittedItem));
        }

        /// <summary>
        /// Check a managed challenge the caller has submitted to save: that it is within their scope, and that it adds or
        /// changes no program execution settings unless they are an administrator. Managed challenges run on the hub.
        /// </summary>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckSubmittedManagedChallenge(
            ICertifyInternalApiClient internalApiClient,
            string resourceActionId,
            ManagedChallenge? update)
        {
            var outOfScope = await CheckManagedChallengeInScope(internalApiClient, resourceActionId, update?.Id, allowNew: true);

            if (outOfScope != null)
            {
                return outOfScope;
            }

            if (!ProgramExecutionSettings.AddsOrChanges(existing: null, update?.ChallengeConfig))
            {
                return null;
            }

            var existing = string.IsNullOrWhiteSpace(update?.Id)
                ? null
                : (await internalApiClient.GetManagedChallenges(SystemAuthContext))?.FirstOrDefault(c => c.Id == update.Id);

            return await CheckProgramExecutionSettingsAllowed(internalApiClient, ProgramExecutionSettings.AddsOrChanges(existing?.ChallengeConfig, update?.ChallengeConfig));
        }

        /// <summary>
        /// Check an instance import: the instance must be within the caller's scope, and a package which would store
        /// program execution settings (in its items, or as bundled scripts) may only be imported by an administrator.
        /// A preview stores nothing.
        /// </summary>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckInstanceImport(
            ICertifyInternalApiClient internalApiClient,
            ManagementAPI mgmtAPI,
            string resourceType,
            string resourceActionId,
            string? instanceId,
            Certify.Models.Config.Migration.ImportRequest? importRequest)
        {
            var outOfScope = await CheckInstanceInScope(internalApiClient, mgmtAPI, resourceType, resourceActionId, instanceId);

            if (outOfScope != null)
            {
                return outOfScope;
            }

            return await CheckProgramExecutionSettingsAllowed(
                internalApiClient,
                importRequest?.IsPreviewMode != true && ProgramExecutionSettings.IsCarriedBy(importRequest?.Package));
        }

        #endregion

        #region Security principals

        /// <summary>
        /// Evaluating which resources another security principal can reach reports that principal's role assignments,
        /// and what they reach regardless of the caller's own scope, so it is limited to callers who may administer
        /// security principals. Any caller may evaluate themselves.
        /// </summary>
        /// <returns>null when the caller may proceed, otherwise the response to return</returns>
        internal async Task<IActionResult?> CheckPrincipalEvaluationAllowed(ICertifyInternalApiClient internalApiClient, string? securityPrincipalId)
        {
            if (string.IsNullOrWhiteSpace(securityPrincipalId)
                || string.Equals(securityPrincipalId, CurrentAuthContext?.UserId, StringComparison.Ordinal))
            {
                return null;
            }

            return await IsAuthorized(internalApiClient, new AccessCheck(default!, ResourceTypes.SecurityPrincipal, StandardResourceActions.SecurityPrincipalList))
                ? null
                : Problem(detail: "Not authorized to evaluate the access of another security principal", statusCode: StatusCodes.Status403Forbidden);
        }

        #endregion

        private ObjectResult ScopeNotEvaluated(string resourceActionId)
            => Problem(detail: $"Could not evaluate the access scope for {resourceActionId}", statusCode: StatusCodes.Status401Unauthorized);

        private ObjectResult NotFoundInScope(string detail)
            => Problem(detail: detail, statusCode: StatusCodes.Status404NotFound);
    }
}
