using System;
using System.Collections.Generic;

namespace Certify.Models.Hub
{
    public enum SecurityPrincipalType
    {
        User = 1,
        Application = 2,
        Group = 3
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
    }

    public class AccessCheck
    {
        public string? SecurityPrincipalId { get; set; } = default!;
        public string ResourceType { get; set; } = default!;
        public string ResourceActionId { get; set; } = default!;
        public string? Identifier { get; set; } = default!;

        public List<string> ScopedAssignedRoles { get; set; } = [];

        public AccessCheck() { }
        public AccessCheck(string? securityPrincipalId, string resourceType, string resourceActionId, string? identifier = null)
        {
            SecurityPrincipalId = securityPrincipalId;
            ResourceType = resourceType;
            ResourceActionId = resourceActionId;
            Identifier = identifier;
        }
    }

    public class AccessTokenCheck
    {
        public AccessToken Token { get; set; } = default!;
        public AccessCheck Check { get; set; } = default!;
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
}
