using Certify.Client;
using Certify.Models.Hub;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// The managed items a caller may see: those the managed item list action reaches, narrowed by the tag scopes
    /// and domain restrictions on the caller's role assignments. Resolved once per caller and then applied to each
    /// item, by the item listing endpoints and by the UI status feed, so that both show a caller the same items.
    /// </summary>
    public sealed class ManagedItemVisibility
    {
        /// <summary>
        /// A caller who may not list managed items at all
        /// </summary>
        public static ManagedItemVisibility None { get; } = new(false, null, []);

        private ManagedItemVisibility(bool canList, List<TagScope>? tagScopes, List<string> domainRules)
        {
            CanList = canList;
            TagScopes = tagScopes;
            DomainRules = domainRules;
        }

        /// <summary>
        /// True when the caller holds the managed item list action
        /// </summary>
        public bool CanList { get; }

        /// <summary>
        /// Tag scopes an item must match one of, or null when unrestricted. An empty set matches nothing, which is
        /// how a role assignment that could not be read fails closed.
        /// </summary>
        public List<TagScope>? TagScopes { get; }

        /// <summary>
        /// Domain Match rules every identifier on an item must satisfy, or empty when unrestricted
        /// </summary>
        public List<string> DomainRules { get; }

        /// <summary>
        /// True when every managed item is visible, so no per-item evaluation is needed
        /// </summary>
        public bool IsUnrestricted => CanList && TagScopes == null && DomainRules.Count == 0;

        /// <summary>
        /// True when an item's tags are needed to decide whether it is visible
        /// </summary>
        public bool RequiresTags => CanList && TagScopes != null;

        /// <summary>
        /// True when an item's identifiers are needed to decide whether it is visible
        /// </summary>
        public bool RequiresIdentifiers => CanList && DomainRules.Count > 0;

        /// <summary>
        /// Whether an item with the given tags and identifiers is visible. A caller restricted to tag scopes never
        /// sees an untagged item, and one restricted to domains never sees an item with no identifiers, as there is
        /// nothing to show it is within their scope.
        /// </summary>
        public bool Permits(IEnumerable<ITaggedValue>? tags, IEnumerable<string?>? identifiers)
        {
            if (!CanList)
            {
                return false;
            }

            if (TagScopes != null && (TagScopes.Count == 0 || !TagScopeFilter.Matches(tags, TagScopes, matchAll: false)))
            {
                return false;
            }

            if (DomainRules.Count > 0)
            {
                var identifierList = identifiers?.Where(i => !string.IsNullOrWhiteSpace(i)).ToList() ?? [];

                // the item's configuration names all of its identifiers, so every one must be within scope
                if (identifierList.Count == 0 || !identifierList.All(i => ResourceAccess.IsIdentifierPermittedByDomainRules(DomainRules, i)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Resolve what the caller may see
        /// </summary>
        public static async Task<ManagedItemVisibility> Resolve(ICertifyInternalApiClient internalApiClient, AuthContext? authContext)
        {
            if (string.IsNullOrWhiteSpace(authContext?.UserId))
            {
                return None;
            }

            var canList = await PrincipalAccess.IsAuthorized(
                internalApiClient,
                authContext,
                new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList));

            if (!canList)
            {
                return None;
            }

            var tagScopes = await PrincipalAccess.GetTagScopes(internalApiClient, authContext);

            var domainRules = await PrincipalAccess.GetDomainRestrictionRules(
                internalApiClient,
                authContext.UserId,
                StandardResourceActions.ManagedItemList,
                authContext.ScopedAssignedRoles);

            if (domainRules == null)
            {
                // fail closed, a transient evaluation error must not promote a restricted principal to unrestricted
                return None;
            }

            return new ManagedItemVisibility(true, tagScopes, domainRules);
        }
    }
}
