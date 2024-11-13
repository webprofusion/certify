using System.Collections.Generic;
using System.Diagnostics;

namespace Certify.Models.Hub
{
    public enum SecurityPrincipleType
    {
        User = 1,
        Application = 2,
        Group
    }

    public enum SecurityPermissionType
    {
        ALLOW = 1,
        DENY = 0
    }

    public class StandardRoles
    {
        public static Role Administrator { get; } = new Role("sysadmin", "Administrator", "Certify Server Administrator",
            policies: new List<string> {
                     StandardPolicies.ManagedItemAdmin,
                     StandardPolicies.StoredCredentialAdmin,
                     StandardPolicies.ManagedChallengeAdmin,
                     StandardPolicies.AccessAdmin
                    });

        public static Role CertificateManager { get; } = new Role("cert_manager", "Certificate Manager", "Can manage and administer all certificates",
            policies: new List<string> {
                     StandardPolicies.ManagedItemAdmin,
                     StandardPolicies.StoredCredentialAdmin
                    });

        public static Role CertificateConsumer { get; } = new Role("cert_consumer", "Certificate Consumer", "User of a given certificate", policies: new List<string> { StandardPolicies.CertificateConsumer });

        public static Role StoredCredentialConsumer { get; } = new Role("storedcredential_consumer", "Stored Credential Fetch Consumer", "Can fetch a decrypted stored credential", policies: new List<string> { StandardPolicies.StoredCredentialConsumer });

        public static Role ManagedChallengeConsumer { get; } = new Role("managedchallenge_consumer", "Managed Challenge Consumer", "Can perform specific managed challenges", policies: new List<string> { StandardPolicies.ManagedChallengeConsumer });
    }

    public class StandardIdentityProviders
    {
        /// <summary>
        /// Identity is stored in the app/service database
        /// </summary>
        public const string INTERNAL = "INTERNAL";

        /// <summary>
        /// Identity is provided by the OS
        /// </summary>
        public const string OS = "OS";

        /// <summary>
        /// Identity is stored in LDAP/AD
        /// </summary>
        public const string LDAP = "LDAP";

        /// <summary>
        /// Identity is provided by OpenID
        /// </summary>
        public const string OID = "OID";
    }

    public class ResourceTypes
    {
        public static string System { get; } = "system";
        public static string SecurityPrinciple { get; } = "securityprinciple";
        public static string Domain { get; } = "domain";
        public static string ManagedItem { get; } = "manageditem";
        public static string Certificate { get; } = "certificate";
        public static string StoredCredential { get; } = "storedcredential";
        public static string CertificateAuthority { get; } = "ca";
        public static string ManagedChallenge { get; } = "managedchallenge";
    }

    public static class StandardResourceActions
    {
        public const string CertificateDownload = "certificate_download";
        public const string CertificateKeyDownload = "certificate_key_download";

        public const string ManagedItemRequest = "manageditem_requester";
        public const string ManagedItemAdd = "manageditem_add";
        public const string ManagedItemList = "manageditem_list";
        public const string ManagedItemUpdate = "manageditem_update";
        public const string ManagedItemDelete = "manageditem_delete";
        public const string ManagedItemTest = "manageditem_test";
        public const string ManagedItemRenew = "manageditem_renew";
        public const string ManagedItemTaskAdd = "manageditem_task_add";
        public const string ManagedItemTaskUpdate = "manageditem_task_update";
        public const string ManagedItemTaskDelete = "manageditem_task_delete";
        public const string ManagedItemLogView = "manageditem_log_view";

        public const string StoredCredentialAdd = "storedcredential_add";
        public const string StoredCredentialUpdate = "storedcredential_update";
        public const string StoredCredentialDelete = "storedcredential_delete";
        public const string StoredCredentialList = "storedcredential_list";
        public const string StoredCredentialDownload = "storedcredential_consumer";

        public const string SecurityPrincipleList = "securityprinciple_list";
        public const string SecurityPrincipleAdd = "securityprinciple_add";
        public const string SecurityPrincipleUpdate = "securityprinciple_update";
        public const string SecurityPrincipleDelete = "securityprinciple_delete";
        public const string SecurityPrinciplePasswordUpdate = "securityprinciple_password_update";

        public const string ManagedChallengeList = "managedchallenge_list";
        public const string ManagedChallengeUpdate = "managedchallenge_update";
        public const string ManagedChallengeDelete = "managedchallenge_update";
        public const string ManagedChallengeRequest = "managedchallenge_request";

    }

    public class StandardPolicies
    {
        public const string AccessAdmin = "access_admin";
        public const string ManagedItemAdmin = "manageditem_admin";
        public const string CertificateConsumer = "certificate_consumer";
        public const string StoredCredentialAdmin = "storedcredential_admin";
        public const string StoredCredentialConsumer = "storedcredential_consumer";
        public const string ManagedChallengeConsumer = "managedchallenge_consumer";
        public const string ManagedChallengeAdmin = "managedchallenge_admin";
    }

    public static class Policies
    {
        public static List<Role> GetStandardRoles()
        {
            return new List<Role>
            {
                StandardRoles.Administrator,
                StandardRoles.CertificateManager,
                StandardRoles.CertificateConsumer,
                StandardRoles.StoredCredentialConsumer,
                StandardRoles.ManagedChallengeConsumer
            };
        }

        public static List<ResourceAction> GetStandardResourceActions()
        {
            return new List<ResourceAction> {

                new(StandardResourceActions.CertificateDownload, "Certificate Download", ResourceTypes.Certificate),
                new(StandardResourceActions.CertificateKeyDownload, "Certificate Private Key Download", ResourceTypes.Certificate),

                new(StandardResourceActions.StoredCredentialAdd, "Add New Stored Credential", ResourceTypes.StoredCredential),
                new(StandardResourceActions.StoredCredentialUpdate, "Update Stored Credential", ResourceTypes.StoredCredential),
                new(StandardResourceActions.StoredCredentialDelete, "Delete Stored Credential", ResourceTypes.StoredCredential),
                new(StandardResourceActions.StoredCredentialList, "List Stored Credentials", ResourceTypes.StoredCredential),
                new(StandardResourceActions.StoredCredentialDownload, "Fetch Decrypted Stored Credential", ResourceTypes.StoredCredential),

                new(StandardResourceActions.SecurityPrincipleList, "List Security Principles", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrincipleAdd, "Add New Security Principle", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrincipleUpdate,"Update Security Principles", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrinciplePasswordUpdate, "Update Security Principle Passwords", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrincipleDelete, "Delete Security Principle", ResourceTypes.SecurityPrinciple),

                new(StandardResourceActions.ManagedItemRequest, "Request New Managed Items", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedItemList, "List Managed Items", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemAdd, "Add Managed Items", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemUpdate, "Update Managed Items", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemDelete, "Delete Managed Items", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedItemTest, "Test Managed Item Renewal Checks", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemRenew, "Request/Renew Managed Items", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedItemTaskAdd, "Add Managed Item Tasks", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemTaskUpdate, "Update Managed Item Tasks", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemTaskDelete, "Delete Managed Item Tasks", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedItemLogView, "View/Download Managed Item Log", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedChallengeList, "List managed challenges", ResourceTypes.ManagedChallenge),
                new(StandardResourceActions.ManagedChallengeUpdate, "Update managed challenge", ResourceTypes.ManagedChallenge),
                new(StandardResourceActions.ManagedChallengeDelete, "Delete managed challenge", ResourceTypes.ManagedChallenge),
                new(StandardResourceActions.ManagedChallengeRequest, "Request to perform a managed challenge response", ResourceTypes.ManagedChallenge),
            };
        }

        public static List<ResourcePolicy> GetStandardPolicies()
        {
            return new List<ResourcePolicy> {
                new() {
                    Id=StandardPolicies.ManagedItemAdmin,
                    Title="Managed Item Administration",
                    SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                        StandardResourceActions.ManagedItemList,
                        StandardResourceActions.ManagedItemAdd,
                        StandardResourceActions.ManagedItemUpdate,
                        StandardResourceActions.ManagedItemDelete,
                        StandardResourceActions.ManagedItemTest,
                        StandardResourceActions.ManagedItemRenew,
                        StandardResourceActions.ManagedItemTaskAdd,
                        StandardResourceActions.ManagedItemTaskUpdate,
                        StandardResourceActions.ManagedItemTaskDelete,
                        StandardResourceActions.ManagedItemLogView
                    }
                },
                new() {
                    Id=StandardPolicies.AccessAdmin,
                    Title="Access Control Administration",
                    SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                       StandardResourceActions.SecurityPrincipleList,
                       StandardResourceActions.SecurityPrincipleAdd,
                       StandardResourceActions.SecurityPrincipleUpdate,
                       StandardResourceActions.SecurityPrincipleDelete,
                       StandardResourceActions.SecurityPrinciplePasswordUpdate
                    }
                },
                new() {
                    Id=StandardPolicies.CertificateConsumer,
                    Title="Consume Certificates",
                    SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                        StandardResourceActions.CertificateDownload,
                        StandardResourceActions.CertificateKeyDownload
                    }
                },
                new() {
                    Id=StandardPolicies.StoredCredentialAdmin,
                    Title="Stored Credential Administration",
                    SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                       StandardResourceActions.StoredCredentialList,
                       StandardResourceActions.StoredCredentialAdd,
                       StandardResourceActions.StoredCredentialUpdate,
                       StandardResourceActions.StoredCredentialDelete
                    }
                },
                new() {
                    Id=StandardPolicies.StoredCredentialConsumer,
                    Title="Stored Credential Consumer",
                    Description="Provides access to fetch a decrypted stored credential.",
                    SecurityPermissionType= SecurityPermissionType.ALLOW,
                    IsResourceSpecific=true,
                    ResourceActions= new List<string>{
                       StandardResourceActions.StoredCredentialDownload
                    }
                },
                 new() {
                    Id=StandardPolicies.ManagedChallengeAdmin,
                    Title="Managed Challenge Administration",
                    SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                        StandardResourceActions.ManagedChallengeList,
                        StandardResourceActions.ManagedChallengeUpdate,
                        StandardResourceActions.ManagedChallengeDelete
                    }
                },
                  new() {
                    Id=StandardPolicies.ManagedChallengeConsumer,
                    Title="Managed Challenge Consumer",
                    Description="Allows consumer to request that a managed challenge be performed.",
                    SecurityPermissionType= SecurityPermissionType.ALLOW,
                    IsResourceSpecific=true,
                    ResourceActions= new List<string>{
                       StandardResourceActions.ManagedChallengeRequest
                    }
                }
            };
        }
    }
}
