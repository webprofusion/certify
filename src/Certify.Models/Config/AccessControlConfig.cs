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

        public static Role Administrator { get; } = new Role("sysadmin", "Administrator", "Certify Server Administrator", policies: new List<string> {
                     "managed_item_admin",
                     "access_admin",
                     "storedcredential_admin"
                    });
        public static Role CertificateManager { get; } = new Role("cert_manager", "Certificate Manager", "Can manage and administer all certificates");
        public static Role CertificateConsumer { get; } = new Role("cert_consumer", "Certificate Consumer", "User of a given certificate", policies: new List<string> { "certificate_consumer" });
        public static Role StoredCredentialConsumer { get; } = new Role("storedcredential_consumer", "Stored Credential Fetch Consumer", "Can fetch a decrypted stored credential", policies: new List<string> { "storedcredential_fetch" });
        public static Role IdentifierController { get; } = new Role("identifier_controller", "Subject Identifier Controller", "Controls certificate access for a given domain/identifier");
        public static Role IdentifierRequestor { get; } = new Role("identifier_requestor", "Subject Identifier Requestor", "Can request new certs for subdomains/subresources on a given domain/identifier ");
    }

    public class StandardProviders
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
        public static string Domain { get; } = "domain";
        public static string ManagedItem { get; } = "manageditem";
        public static string Certificate { get; } = "certificate";
        public static string StoredCredential { get; } = "storedcredential";
        public static string CertificateAuthority { get; } = "ca";
    }

    public static class StandardResourceActions
    {
        public const string CertificateDownload = "certificate_download";
        public const string ManagedItemAdd = "manageditem_add";
        public const string ManagedItemList = "manageditem_list";
    }

    public static class Policies
    {
        public static List<ResourceAction> GetStandardResourceActions()
        {
            return new List<ResourceAction> {

                new ResourceAction(StandardResourceActions.CertificateDownload, "Certificate Download", ResourceTypes.Certificate),

                new ResourceAction("storedcredential_add", "Add New Stored Credential", ResourceTypes.StoredCredential),
                new ResourceAction("storedcredential_update", "Update Stored Credential", ResourceTypes.StoredCredential),
                new ResourceAction("storedcredential_delete", "Delete Stored Credential", ResourceTypes.StoredCredential),
                new ResourceAction("storedcredential_fetch", "Fetch Decrypted Stored Credential", ResourceTypes.StoredCredential),

                new ResourceAction("securityprinciple_add", "Add New Security Principle", ResourceTypes.System),
                new ResourceAction("securityprinciple_update", "UPdate Security Principles", ResourceTypes.System),
                new ResourceAction("securityprinciple_changepassword", "Update Security Principle Passwords", ResourceTypes.System),
                new ResourceAction("securityprinciple_delete", "Delete Security Principle", ResourceTypes.System),

                new ResourceAction("manageditem_requester", "Request New Managed Items", ResourceTypes.ManagedItem),
                new ResourceAction("manageditem_add", "Add Managed Items", ResourceTypes.ManagedItem),
                new ResourceAction("manageditem_list", "List Managed Items", ResourceTypes.ManagedItem),
                new ResourceAction("manageditem_update", "Update Managed Items", ResourceTypes.ManagedItem),
                new ResourceAction("manageditem_delete", "Delete Managed Items", ResourceTypes.ManagedItem),
                new ResourceAction("manageditem_test", "Test Managed Item Renewal Checks", ResourceTypes.ManagedItem),
                new ResourceAction("manageditem_renew", "Request/Renew Managed Items", ResourceTypes.ManagedItem),
                new ResourceAction("manageditem_updatetasks", "Add or Update Managed Item Tasks", ResourceTypes.ManagedItem),
                new ResourceAction("manageditem_updatescript", "Add or Update Managed Item Deployment Scripts", ResourceTypes.ManagedItem),
                new ResourceAction("manageditem_log", "View/Download Managed Item Log", ResourceTypes.ManagedItem),
            };
        }

        public static List<ResourcePolicy> GetStandardPolicies()
        {
            return new List<ResourcePolicy> {
                new ResourcePolicy{ Id="managed_item_admin", Title="Managed Item Administration", SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                        StandardResourceActions.ManagedItemList,
                        StandardResourceActions.ManagedItemAdd,
                        "manageditem_update",
                        "manageditem_delete",
                        "manageditem_test",
                        "manageditem_renew",
                        "manageditem_updatetasks",
                        "manageditem_updatescript",
                        "manageditem_log",
                    }
                },
                new ResourcePolicy{ Id="access_admin", Title="Access Control Administration", SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                        "securityprinciple_add",
                        "securityprinciple_update",
                        "securityprinciple_changepassword",
                        "securityprinciple_delete"
                    }
                },
                new ResourcePolicy{ Id="certificate_consumer", Title="Consume Certificates", SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                        StandardResourceActions.CertificateDownload,
                        "certificate_key_download"
                    }
                },
                new ResourcePolicy{ Id="storedcredential_admin", Title="Stored Credential Administration", SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                        "storedcredential_add",
                        "storedcredential_update",
                        "storedcredential_delete"
                    }
                },
                new ResourcePolicy{ Id="storedcredential_fetch", Title="Stored Credential Fetch", Description="Provides access to fetch a decrypted stored credential.", SecurityPermissionType= SecurityPermissionType.ALLOW,
                    ResourceActions= new List<string>{
                        "storedcredential_fetch"
                    }
                }
            };
        }
    }
}
