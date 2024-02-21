using System.Collections.Generic;

namespace Certify.Models.Config.AccessControl
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
                     StandardPolicies.AccessAdmin
                    });

        public static Role CertificateManager { get; } = new Role("cert_manager", "Certificate Manager", "Can manage and administer all certificates",
            policies: new List<string> {
                     StandardPolicies.ManagedItemAdmin,
                     StandardPolicies.StoredCredentialAdmin
                    });

        public static Role CertificateConsumer { get; } = new Role("cert_consumer", "Certificate Consumer", "User of a given certificate", policies: new List<string> { StandardPolicies.CertificateConsumer });

        public static Role StoredCredentialConsumer { get; } = new Role("storedcredential_consumer", "Stored Credential Fetch Consumer", "Can fetch a decrypted stored credential", policies: new List<string> { StandardPolicies.StoredCredentialConsumer });
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
    }

    public static class StandardResourceActions
    {
        public const string CertificateDownload = "certificate_download";
        public const string CertificateKeyDownload = "certificate_key_download";

        public const string ManagedItemRequester = "manageditem_requester";
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

    }

    public class StandardPolicies
    {
        public const string AccessAdmin = "access_admin";
        public const string ManagedItemAdmin = "managed_item_admin";
        public const string CertificateConsumer = "certificate_consumer";
        public const string StoredCredentialAdmin = "storedcredential_admin";
        public const string StoredCredentialConsumer = "storedcredential_consumer";
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
                StandardRoles.StoredCredentialConsumer
            };
        }

        public static List<ResourceAction> GetStandardResourceActions()
        {
            return new List<ResourceAction> {

                new ResourceAction(StandardResourceActions.CertificateDownload, "Certificate Download", ResourceTypes.Certificate),
                new ResourceAction(StandardResourceActions.CertificateKeyDownload, "Certificate Private Key Download", ResourceTypes.Certificate),

                new ResourceAction(StandardResourceActions.StoredCredentialAdd, "Add New Stored Credential", ResourceTypes.StoredCredential),
                new ResourceAction(StandardResourceActions.StoredCredentialUpdate, "Update Stored Credential", ResourceTypes.StoredCredential),
                new ResourceAction(StandardResourceActions.StoredCredentialDelete, "Delete Stored Credential", ResourceTypes.StoredCredential),
                new ResourceAction(StandardResourceActions.StoredCredentialList, "List Stored Credentials", ResourceTypes.StoredCredential),
                new ResourceAction(StandardResourceActions.StoredCredentialDownload, "Fetch Decrypted Stored Credential", ResourceTypes.StoredCredential),

                new ResourceAction(StandardResourceActions.SecurityPrincipleList, "List Security Principles", ResourceTypes.SecurityPrinciple),
                new ResourceAction(StandardResourceActions.SecurityPrincipleAdd, "Add New Security Principle", ResourceTypes.SecurityPrinciple),
                new ResourceAction(StandardResourceActions.SecurityPrincipleUpdate,"Update Security Principles", ResourceTypes.SecurityPrinciple),
                new ResourceAction(StandardResourceActions.SecurityPrinciplePasswordUpdate, "Update Security Principle Passwords", ResourceTypes.SecurityPrinciple),
                new ResourceAction(StandardResourceActions.SecurityPrincipleDelete, "Delete Security Principle", ResourceTypes.SecurityPrinciple),

                new ResourceAction(StandardResourceActions.ManagedItemRequester, "Request New Managed Items", ResourceTypes.ManagedItem),

                new ResourceAction(StandardResourceActions.ManagedItemList, "List Managed Items", ResourceTypes.ManagedItem),
                new ResourceAction(StandardResourceActions.ManagedItemAdd, "Add Managed Items", ResourceTypes.ManagedItem),
                new ResourceAction(StandardResourceActions.ManagedItemUpdate, "Update Managed Items", ResourceTypes.ManagedItem),
                new ResourceAction(StandardResourceActions.ManagedItemDelete, "Delete Managed Items", ResourceTypes.ManagedItem),

                new ResourceAction(StandardResourceActions.ManagedItemTest, "Test Managed Item Renewal Checks", ResourceTypes.ManagedItem),
                new ResourceAction(StandardResourceActions.ManagedItemRenew, "Request/Renew Managed Items", ResourceTypes.ManagedItem),

                new ResourceAction(StandardResourceActions.ManagedItemTaskAdd, "Add Managed Item Tasks", ResourceTypes.ManagedItem),
                new ResourceAction(StandardResourceActions.ManagedItemTaskUpdate, "Update Managed Item Tasks", ResourceTypes.ManagedItem),
                new ResourceAction(StandardResourceActions.ManagedItemTaskDelete, "Delete Managed Item Tasks", ResourceTypes.ManagedItem),

                new ResourceAction(StandardResourceActions.ManagedItemLogView, "View/Download Managed Item Log", ResourceTypes.ManagedItem),
            };
        }

        public static List<ResourcePolicy> GetStandardPolicies()
        {
            return new List<ResourcePolicy> {
                new ResourcePolicy{
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
                new ResourcePolicy{
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
                new ResourcePolicy{
                    Id=StandardPolicies.CertificateConsumer,
                    Title="Consume Certificates",
                    SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                        StandardResourceActions.CertificateDownload,
                        StandardResourceActions.CertificateKeyDownload
                    }
                },
                new ResourcePolicy{
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
                new ResourcePolicy{
                    Id=StandardPolicies.StoredCredentialConsumer,
                    Title="Stored Credential Consumer",
                    Description="Provides access to fetch a decrypted stored credential.",
                    SecurityPermissionType= SecurityPermissionType.ALLOW,
                    IsResourceSpecific=true,
                    ResourceActions= new List<string>{
                       StandardResourceActions.StoredCredentialDownload
                    }
                }
            };
        }
    }
}
