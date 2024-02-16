using System;
using System.Collections.Generic;

namespace Certify.Models.Config.AccessControl
{
    public class AccessStoreItem
    {
        public AccessStoreItem()
        {
            Id = Guid.NewGuid().ToString();
        }
        public string Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
    }

    /// <summary>
    /// A Security Principle is a user or service account which can be assigned roles and other permissions
    /// </summary>
    public class SecurityPrinciple : AccessStoreItem
    {

        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Email { get; set; }

        /// <summary>
        /// Provider e.g. if identifier is a mapping to an external AD/LDAP group or user
        /// </summary>
        public string? Provider { get; set; }

        /// <summary>
        /// If principle is externally controlled, this is the identifier from the external system
        /// </summary>
        public string? ExternalIdentifier { get; set; }

        public SecurityPrincipleType? PrincipleType { get; set; }

        public string? AuthKey { get; set; }

        public string AvatarUrl { get; set; } = string.Empty;
    }

    public class Role : AccessStoreItem
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
    /// A role assigned to a security principle
    /// </summary>
    public class AssignedRole : AccessStoreItem
    {
        /// <summary>
        /// Defines the role to be assigned 
        /// </summary>
        public string? RoleId { get; set; }

        /// <summary>
        /// Specific security principle assigned to the role
        /// </summary>
        public string? SecurityPrincipleId { get; set; }

        public List<Resource>? IncludedResources { get; set; }
    }

    /// <summary>
    /// Defines a restricted resource
    /// </summary>
    public class Resource : AccessStoreItem
    {
        /// <summary>
        /// Type of this resource
        /// </summary>
        public string? ResourceType { get; set; }

        /// <summary>
        /// Identifier for this resource, can include wildcards for domains etc
        /// </summary>
        public string? Identifier { get; set; }
    }

    public class ResourcePolicy : AccessStoreItem
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
    public class ResourceAction : AccessStoreItem
    {
        public ResourceAction(string id, string title, string resourceType)
        {
            Id = id;
            Title = title;
            ResourceType = resourceType;
        }

        public string? ResourceType { get; set; }
    }
    public class SecurityPrincipleAssignedRoleUpdate
    {
        public string SecurityPrincipleId { get; set; } = string.Empty;
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
