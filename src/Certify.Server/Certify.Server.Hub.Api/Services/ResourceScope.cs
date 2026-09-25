using Certify.Client;
using Certify.Models.Hub;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// The resources a caller may reach with a resource action: those the action reaches, narrowed by the tag scopes,
    /// domain restrictions and included resources on the role assignments which grant it. Resolved once per caller and
    /// action, then applied to each resource.
    ///
    /// Each authorizing assignment is evaluated as a unit (see <see cref="ResourceAccess.IsResourcePermitted"/>), so a
    /// resource is reachable only when a single assignment grants both its tags and its identifiers. For the managed
    /// item list action this is what the item listing endpoints and the UI status feed show, so both show a caller the
    /// same items; the endpoints acting on a single resource resolve it for their own action, so a resource the caller
    /// cannot reach cannot be read or changed by its id either.
    /// </summary>
    public sealed class ResourceScope
    {
        /// <summary>
        /// A caller who may not perform the action at all, or whose scope for it could not be read
        /// </summary>
        public static ResourceScope None { get; } = new(null, null);

        private readonly ResourceAccessScope? _scope;
        private readonly string? _resourceType;

        private ResourceScope(ResourceAccessScope? scope, string? resourceType)
        {
            _scope = scope;
            _resourceType = resourceType;
        }

        /// <summary>
        /// True when the caller holds the action and their scope for it could be read
        /// </summary>
        public bool HasAction => _scope?.HasAccess == true;

        /// <summary>
        /// True when every resource is reachable, so no per-resource evaluation is needed
        /// </summary>
        public bool IsUnrestricted => HasAction && ResourceAccess.IsScopeUnrestricted(_scope, _resourceType);

        /// <summary>
        /// True when a resource's tags are needed to decide whether it is reachable
        /// </summary>
        public bool RequiresTags => HasAction && !IsUnrestricted && (_scope!.AuthorizingRoles ?? []).Any(r => r?.ScopedTags?.Count > 0);

        /// <summary>
        /// True when a resource's identifiers are needed to decide whether it is reachable
        /// </summary>
        public bool RequiresIdentifiers => HasAction && ResourceAccess.GetDomainRestrictionRules(_scope!.AuthorizingRoles).Count > 0;

        /// <summary>
        /// Whether a resource carrying identifiers (a managed item or certificate) is reachable. A caller restricted to
        /// tag scopes never reaches an untagged resource, and one restricted to domains never reaches a resource with no
        /// identifiers, as there is nothing to show it is within their scope.
        /// </summary>
        public bool Permits(IEnumerable<ITaggedValue>? tags, IEnumerable<string?>? identifiers)
            => HasAction && (IsUnrestricted || ResourceAccess.IsResourcePermitted(_scope, tags ?? [], identifiers ?? []));

        /// <summary>
        /// Whether every one of the given identifiers is within the caller's domain restrictions, without considering
        /// tags. This alone decides a managed item configuration the caller is submitting, which has no tags until it
        /// has been saved.
        /// </summary>
        public bool PermitsIdentifiers(IEnumerable<string?>? identifiers)
            => HasAction && (IsUnrestricted || ResourceAccess.IsResourcePermitted(_scope, resourceTags: null, identifiers ?? []));

        /// <summary>
        /// Whether a resource without identifiers (a stored credential, managed challenge or tag) is reachable, from
        /// its tags and, for a role restricted to specific resources, its id.
        /// </summary>
        public bool PermitsTags(IEnumerable<ITaggedValue>? tags, string? resourceId = null)
            => HasAction && (IsUnrestricted || ResourceAccess.IsResourcePermitted(_scope, tags ?? [], identifiers: null, _resourceType, resourceId));

        /// <summary>
        /// Whether a managed instance is reachable, from its own tags and the managed items it holds
        /// </summary>
        public bool PermitsInstance(IEnumerable<ITaggedValue>? instanceTags, IEnumerable<(IEnumerable<ITaggedValue>? Tags, IEnumerable<string?>? Identifiers)>? instanceItems)
            => HasAction && (IsUnrestricted || ResourceAccess.IsInstancePermitted(_scope, instanceTags, instanceItems));

        /// <summary>
        /// The managed instances, of those given, which are reachable. An instance is reachable when it carries the
        /// tags of a tag scoped assignment granting the action, or holds a managed item that assignment permits, so a
        /// scoped caller reaches the instances their items are on. Tags are read once for all of the instances.
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="instanceItems">the hub's cached items for each instance</param>
        /// <param name="instanceIds">the instances to evaluate</param>
        public async Task<HashSet<string>> GetInstancesInScope(
            ICertifyInternalApiClient internalApiClient,
            IEnumerable<ManagedInstanceItems> instanceItems,
            IEnumerable<string?> instanceIds)
        {
            var ids = instanceIds
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!HasAction || ids.Count == 0)
            {
                return [];
            }

            if (IsUnrestricted)
            {
                return ids;
            }

            var instanceTags = await HubItemTags.GetAllItemTags(internalApiClient, TaggedItemTypes.ManagedInstance);
            var itemTags = RequiresTags ? await HubItemTags.GetAllItemTags(internalApiClient, TaggedItemTypes.ManagedCertificate) : null;
            var itemsByInstance = instanceItems
                .Where(i => !string.IsNullOrWhiteSpace(i.InstanceId))
                .GroupBy(i => i.InstanceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.SelectMany(i => i.Items ?? []).ToList(), StringComparer.OrdinalIgnoreCase);

            (IEnumerable<ITaggedValue>? Tags, IEnumerable<string?>? Identifiers) Describe(string instanceId, Certify.Models.ManagedCertificate item)
                => (itemTags == null ? [] : HubItemTags.ForItem(itemTags, instanceId, item.Id), item.GetCertificateIdentifiers().Select(i => i.Value));

            return ids
                .Where(id => PermitsInstance(
                    HubItemTags.ForItem(instanceTags, id, id),
                    (itemsByInstance.TryGetValue(id, out var items) ? items : []).Select(item => Describe(id, item))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolve what the caller may reach with the given resource action
        /// </summary>
        public static Task<ResourceScope> Resolve(
            ICertifyInternalApiClient internalApiClient,
            AuthContext? authContext,
            string resourceType,
            string resourceActionId)
            => ResolveForPrincipal(internalApiClient, authContext?.UserId, authContext?.ScopedAssignedRoles, resourceType, resourceActionId);

        /// <summary>
        /// Resolve what a given security principal may reach with the given resource action, narrowed to the role
        /// assignments an API access token is scoped to where one is given
        /// </summary>
        public static async Task<ResourceScope> ResolveForPrincipal(
            ICertifyInternalApiClient internalApiClient,
            string? securityPrincipalId,
            ICollection<string>? scopedAssignedRoles,
            string resourceType,
            string resourceActionId)
        {
            if (string.IsNullOrWhiteSpace(securityPrincipalId))
            {
                return None;
            }

            var check = new AccessCheck
            {
                SecurityPrincipalId = securityPrincipalId,
                ResourceType = resourceType,
                ResourceActionId = resourceActionId
            };

            if (scopedAssignedRoles?.Count > 0)
            {
                check.ScopedAssignedRoles = scopedAssignedRoles.ToList();
            }

            try
            {
                var scope = await internalApiClient.EvaluateAccessScope(check, PrincipalAccess.SystemAuthContext);

                return scope?.HasAccess == true ? new ResourceScope(scope, resourceType) : None;
            }
            catch (Exception)
            {
                // fail closed, a transient evaluation error must not promote a restricted principal to unrestricted
                return None;
            }
        }
    }
}
