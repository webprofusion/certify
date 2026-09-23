using Certify.Client;
using Certify.Models.Hub;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// The managed items a caller may reach with a managed item action: those the action reaches, narrowed by the
    /// tag scopes and domain restrictions on the caller's role assignments. Resolved once per caller and then
    /// applied to each item. For the list action this is what the item listing endpoints and the UI status feed
    /// show, so both show a caller the same items; the endpoints acting on a single item resolve it for their own
    /// action, so an item the caller cannot reach cannot be read or changed by its id either.
    /// </summary>
    public sealed class ManagedItemVisibility
    {
        /// <summary>
        /// A caller who may not perform the action at all, or whose scope for it could not be read
        /// </summary>
        public static ManagedItemVisibility None { get; } = new(false, null, []);

        private ManagedItemVisibility(bool hasAction, List<TagScope>? tagScopes, List<string> domainRules)
        {
            HasAction = hasAction;
            TagScopes = tagScopes;
            DomainRules = domainRules;
        }

        /// <summary>
        /// True when the caller holds the action and their scope for it could be read
        /// </summary>
        public bool HasAction { get; }

        /// <summary>
        /// Tag scopes an item must match one of, or null when unrestricted
        /// </summary>
        public List<TagScope>? TagScopes { get; }

        /// <summary>
        /// Domain Match rules every identifier on an item must satisfy, or empty when unrestricted
        /// </summary>
        public List<string> DomainRules { get; }

        /// <summary>
        /// True when every managed item is visible, so no per-item evaluation is needed
        /// </summary>
        public bool IsUnrestricted => HasAction && TagScopes == null && DomainRules.Count == 0;

        /// <summary>
        /// True when an item's tags are needed to decide whether it is visible
        /// </summary>
        public bool RequiresTags => HasAction && TagScopes != null;

        /// <summary>
        /// True when an item's identifiers are needed to decide whether it is visible
        /// </summary>
        public bool RequiresIdentifiers => HasAction && DomainRules.Count > 0;

        /// <summary>
        /// Whether an item with the given tags and identifiers is visible. A caller restricted to tag scopes never
        /// sees an untagged item, and one restricted to domains never sees an item with no identifiers, as there is
        /// nothing to show it is within their scope.
        /// </summary>
        public bool Permits(IEnumerable<ITaggedValue>? tags, IEnumerable<string?>? identifiers)
        {
            if (!HasAction)
            {
                return false;
            }

            if (TagScopes != null && !TagScopeFilter.Matches(tags, TagScopes, matchAll: false))
            {
                return false;
            }

            return PermitsIdentifiers(identifiers);
        }

        /// <summary>
        /// Whether every one of the given identifiers is within the caller's domain restrictions. This alone decides
        /// a managed item configuration the caller is submitting, which has no tags until it has been saved.
        /// </summary>
        public bool PermitsIdentifiers(IEnumerable<string?>? identifiers)
        {
            if (!HasAction)
            {
                return false;
            }

            if (DomainRules.Count == 0)
            {
                return true;
            }

            var identifierList = identifiers?.Where(i => !string.IsNullOrWhiteSpace(i)).ToList() ?? [];

            // the item's configuration names all of its identifiers, so every one must be within scope
            return identifierList.Count > 0
                && identifierList.All(i => ResourceAccess.IsIdentifierPermittedByDomainRules(DomainRules, i));
        }

        /// <summary>
        /// Resolve what the caller may reach with the given managed item action, by default what they may list
        /// </summary>
        public static async Task<ManagedItemVisibility> Resolve(
            ICertifyInternalApiClient internalApiClient,
            AuthContext? authContext,
            string resourceActionId = StandardResourceActions.ManagedItemList)
        {
            if (string.IsNullOrWhiteSpace(authContext?.UserId))
            {
                return None;
            }

            var hasAction = await PrincipalAccess.IsAuthorized(
                internalApiClient,
                authContext,
                new AccessCheck(default!, ResourceTypes.ManagedItem, resourceActionId));

            if (!hasAction)
            {
                return None;
            }

            var tagScopes = await PrincipalAccess.GetTagScopes(internalApiClient, authContext);

            if (tagScopes?.Count == 0)
            {
                // the role assignments could not be read, so there is nothing to show which items are in scope
                return None;
            }

            var domainRules = await PrincipalAccess.GetDomainRestrictionRules(
                internalApiClient,
                authContext.UserId,
                resourceActionId,
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
