using System;
using System.Collections.Generic;
using System.Linq;

namespace Certify.Models.Hub
{
    public enum SecurityPrincipalType
    {
        User = 1,
        Application = 2,
        Group = 3,
        ManagedInstance = 4
    }

    public enum SecurityPermissionType
    {
        ALLOW = 1,
        DENY = 0
    }

    /// <summary>
    /// A Security Principal is a user or service account which can be assigned roles and other permissions
    /// </summary>
    public class SecurityPrincipal : ConfigurationStoreItem
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Email { get; set; }

        /// <summary>
        /// Provider e.g. if identifier is a mapping to an external AD/LDAP group or user
        /// </summary>
        public string? Provider { get; set; }

        /// <summary>
        /// If principal is externally controlled, this is the identifier from the external system
        /// </summary>
        public string? ExternalIdentifier { get; set; }

        public SecurityPrincipalType PrincipalType { get; set; } = SecurityPrincipalType.User;

        public string? AuthKey { get; set; }

        public string AvatarUrl { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; }

        /// <summary>
        /// Tags associated with this security principal (populated when querying with tag info)
        /// </summary>
        public List<TagSummary>? Tags { get; set; }
    }

    /// <summary>
    /// A role is a collection of policies which can be assigned to a security principal via AssignedRole
    /// </summary>
    public class Role : ConfigurationStoreItem
    {
        public List<string> Policies { get; set; } = new List<string>();
        public Role(string id, string title, string description, List<string>? policies = null)
        {
            Id = id;
            Title = title;
            Description = description;

            if (policies != null)
            {
                Policies = policies;
            }
        }
    }

    /// <summary>
    /// A role assigned to a security principal, optionally specific to a set of resources
    /// </summary>
    public class AssignedRole : ConfigurationStoreItem
    {
        /// <summary>
        /// Defines the role to be assigned 
        /// </summary>
        public string RoleId { get; set; } = default!;

        /// <summary>
        /// Specific security principal assigned to the role
        /// </summary>
        public string SecurityPrincipalId { get; set; } = default!;

        public List<Resource>? IncludedResources { get; set; } = [];

        /// <summary>
        /// If set, this role assignment is scoped to resources matching these tag scopes.
        /// Null/empty means no tag-based restriction.
        /// </summary>
        public List<TagScope>? ScopedTags { get; set; }

        /// <summary>
        /// If true, require all tag scopes to match (AND). Default false (OR).
        /// </summary>
        public bool RequireAllScopedTags { get; set; } = false;
    }

    public class AccessCheck
    {
        public string? SecurityPrincipalId { get; set; } = default!;
        public string ResourceType { get; set; } = default!;
        public string ResourceActionId { get; set; } = default!;
        public string? Identifier { get; set; } = default!;

        public List<string> ScopedAssignedRoles { get; set; } = [];

        /// <summary>
        /// Tags on the resource being accessed (for scope validation)
        /// </summary>
        public List<TagSummary>? ResourceTags { get; set; }

        /// <summary>
        /// When evaluating access scope for resource selection, allow tag-scoped roles to also
        /// use untagged resources. Used by hub preference for legacy managed-challenge behaviour.
        /// </summary>
        public bool AllowUnscopedResources { get; set; }

        public AccessCheck() { }
        public AccessCheck(string? securityPrincipalId, string resourceType, string resourceActionId, string? identifier = null)
        {
            SecurityPrincipalId = securityPrincipalId;
            ResourceType = resourceType;
            ResourceActionId = resourceActionId;
            Identifier = identifier;
        }
    }

    /// <summary>
    /// Resolved access for a security principal against a resource action, including authorizing
    /// assigned roles and whether access is unrestricted or tag-scoped.
    /// </summary>
    public class ResourceAccessScope
    {
        /// <summary>
        /// True when the principal has at least one role granting the requested action.
        /// </summary>
        public bool HasAccess { get; set; }

        /// <summary>
        /// True when at least one authorizing role is not tag-scoped (unrestricted for this action).
        /// </summary>
        public bool IsUnrestricted { get; set; }

        /// <summary>
        /// Authorizing assigned roles used to evaluate access (already filtered to the requested action
        /// and optional scoped assigned-role ids from an access token/EAB binding).
        /// </summary>
        public List<AssignedRole> AuthorizingRoles { get; set; } = [];

        /// <summary>
        /// When true, principals whose authorizing roles are tag-scoped may also use untagged resources.
        /// </summary>
        public bool AllowUnscopedResources { get; set; }

        /// <summary>
        /// True when resource selection must be filtered by role tag scopes.
        /// </summary>
        public bool RequiresTagFiltering => HasAccess && !IsUnrestricted;
    }

    public class AccessTokenCheck
    {
        public AccessToken Token { get; set; } = default!;
        public AccessCheck Check { get; set; } = default!;
    }

    public class AccessTokenAuthorizationContext
    {
        public string SecurityPrincipalId { get; set; } = default!;
        public List<string> ScopedAssignedRoles { get; set; } = [];
    }

    public class AccessTokenTypes
    {
        public const string Simple = "simple";
    }
    public class AccessToken : ConfigurationStoreItem
    {
        public string? TokenType { get; set; } = default!;
        public string Secret { get; set; } = default!;
        public string ClientId { get; set; } = default!;

        public DateTimeOffset? DateCreated { get; set; }
        public DateTimeOffset? DateExpiry { get; set; }
        public DateTimeOffset? DateRevoked { get; set; }
    }
    public class AssignedAccessToken : ConfigurationStoreItem
    {
        public string SecurityPrincipalId { get; set; } = default!;

        /// <summary>
        /// Optional list of Assigned Roles this access token is scoped to. Note this is not the RoleID but the AssignedRoleID.
        /// </summary>
        public List<string> ScopedAssignedRoles { get; set; } = [];

        /// <summary>
        /// List of access tokens assigned
        /// </summary>
        public List<AccessToken> AccessTokens { get; set; } = [];
    }

    /// <summary>
    /// Defines a restricted resource
    /// </summary>
    public class Resource : ConfigurationStoreItem
    {
        /// <summary>
        /// Type of this resource
        /// </summary>
        public string ResourceType { get; set; } = default!;

        /// <summary>
        /// Identifier for this resource, can include wildcards for domains etc
        /// </summary>
        public string Identifier { get; set; } = default!;
    }

    public class ResourcePolicy : ConfigurationStoreItem
    {

        /// <summary>
        /// Whether policy is allow or deny for the set of actions
        /// </summary>
        public SecurityPermissionType SecurityPermissionType { get; set; } = SecurityPermissionType.DENY;

        /// <summary>
        /// List of actions to apply to this policy
        /// </summary>
        public List<string> ResourceActions { get; set; } = new List<string>();

        /// <summary>
        /// If true, this policy requires on or more specific identified resources and cannot be applied to all resources
        /// </summary>
        public bool IsResourceSpecific { get; set; }
    }

    /// <summary>
    ///  Specific system action which may be allowed/disallowed on a specific type of resource
    /// </summary>
    public class ResourceAction : ConfigurationStoreItem
    {
        public ResourceAction(string id, string title, string resourceType)
        {
            Id = id;
            Title = title;
            ResourceType = resourceType;
        }

        public string? ResourceType { get; set; }
    }
    public class SecurityPrincipalAssignedRoleUpdate
    {
        public string SecurityPrincipalId { get; set; } = string.Empty;
        public List<AssignedRole> AddedAssignedRoles { get; set; } = new List<AssignedRole>();
        public List<AssignedRole> RemovedAssignedRoles { get; set; } = new List<AssignedRole>();
    }

    public class RoleStatus
    {
        public IEnumerable<AssignedRole> AssignedRoles { get; set; } = new List<AssignedRole>();
        public IEnumerable<Role> Roles { get; set; } = new List<Role>();
        public IEnumerable<ResourcePolicy> Policies { get; set; } = new List<ResourcePolicy>();
        public IEnumerable<ResourceAction> Action { get; set; } = new List<ResourceAction>();
    }

    /// <summary>
    /// Shared access-scope helpers used by Access Control and resource consumers.
    /// </summary>
    public static class ResourceAccess
    {
        /// <summary>
        /// True when the given domain identifier is permitted by the included domain resources on the
        /// authorizing roles. When no authorizing role carries any domain-typed IncludedResources the
        /// check is skipped (unrestricted).
        ///
        /// Each domain resource identifier is treated as a Domain Match rule and evaluated by the shared
        /// <see cref="DomainMatchRules"/> implementation, so wildcard rules (*.example.com), multiple rules
        /// in one value and case insensitivity all behave as they do elsewhere in the product.
        /// </summary>
        public static bool IsIdentifierPermittedByDomainRestrictions(
            IEnumerable<AssignedRole>? authorizingRoles,
            string? identifier)
        {
            var domainRules = GetDomainRestrictionRules(authorizingRoles);

            // no domain restrictions on any authorizing role, so unrestricted
            if (domainRules.Count == 0)
            {
                return true;
            }

            return IsIdentifierPermittedByDomainRules(domainRules, identifier);
        }

        /// <summary>
        /// The set of Domain Match rules restricting the given roles, taken from any domain-typed
        /// IncludedResources. An empty result means no domain restrictions apply.
        /// </summary>
        public static List<string> GetDomainRestrictionRules(IEnumerable<AssignedRole>? authorizingRoles)
        {
            return (authorizingRoles ?? [])
                .Where(a => a != null)
                .SelectMany(a => a.IncludedResources ?? [])
                .Where(r => r?.ResourceType == ResourceTypes.Domain && !string.IsNullOrWhiteSpace(r.Identifier))
                .Select(r => r.Identifier)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// True when the identifier is permitted by at least one of the given Domain Match rules.
        /// A wildcard identifier (e.g. *.example.com) must be granted by an explicit wildcard rule, as a
        /// rule for the root domain alone does not imply authority over all of its subdomains.
        /// </summary>
        public static bool IsIdentifierPermittedByDomainRules(IEnumerable<string>? domainRules, string? identifier)
        {
            var rules = domainRules?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList() ?? [];

            if (rules.Count == 0 || string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            var requested = identifier!.Trim().ToLowerInvariant();

            if (requested.StartsWith("*", StringComparison.Ordinal))
            {
                // only an explicit wildcard rule grants a wildcard identifier
                return rules.Any(r => DomainMatchRules.ParseRules(r).Contains(requested));
            }

            return rules.Any(r => DomainMatchRules.IsMatch(r, identifier));
        }

        /// <summary>
        /// True when a concrete resource (via tags) is within the resolved access scope.
        /// </summary>
        public static bool IsResourceInScope(ResourceAccessScope? scope, IEnumerable<TagSummary>? resourceTags)
        {
            if (scope == null || !scope.HasAccess)
            {
                return false;
            }

            if (scope.IsUnrestricted)
            {
                return true;
            }

            var tags = resourceTags?.ToList() ?? [];

            if (tags.Count == 0)
            {
                return scope.AllowUnscopedResources;
            }

            foreach (var role in scope.AuthorizingRoles.Where(r => r.ScopedTags?.Count > 0))
            {
                if (IsResourceTagScopeMatch(tags, role.ScopedTags, role.RequireAllScopedTags))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if resource tags match the required tag scope for access control.
        /// Category keys are normalised to lowercase when stored, and tag values are user entered,
        /// so both are compared case-insensitively to avoid casing differences silently denying access.
        /// </summary>
        public static bool IsResourceTagScopeMatch(List<TagSummary>? resourceTags, List<TagScope>? scopedTags, bool requireAll)
        {
            if (scopedTags == null || scopedTags.Count == 0)
            {
                return true;
            }

            if (resourceTags == null || resourceTags.Count == 0)
            {
                return false;
            }

            if (requireAll)
            {
                foreach (var scope in scopedTags)
                {
                    if (!resourceTags.Any(tag => IsTagScopeMatch(tag, scope)))
                    {
                        return false;
                    }
                }

                return true;
            }

            return scopedTags.Any(scope => resourceTags.Any(tag => IsTagScopeMatch(tag, scope)));
        }

        /// <summary>
        /// True when a single resource tag satisfies a single tag scope (null scope value matches any value in the category).
        /// </summary>
        private static bool IsTagScopeMatch(TagSummary tag, TagScope scope)
        {
            if (!string.Equals(tag.CategoryKey, scope.CategoryKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return scope.Value == null
                || string.Equals(tag.Value, scope.Value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Convert item tags to summaries used by scope matching.
        /// </summary>
        public static List<TagSummary> ToTagSummaries(IEnumerable<ItemTag>? tags)
        {
            if (tags == null)
            {
                return [];
            }

            return tags
                .Select(t => new TagSummary { CategoryKey = t.CategoryKey, Value = t.Value })
                .ToList();
        }
    }
}
