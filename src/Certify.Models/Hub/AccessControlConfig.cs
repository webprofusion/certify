using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Management;

namespace Certify.Models.Hub
{
    public class StandardRoles
    {
        internal static Role BackupOperator { get; } = new Role("backup_operator_role", "Backup Operator", "Can perform import and export operations",
            policies: new List<string> {
                StandardPolicies.ManagedInstanceSystemExport,
                StandardPolicies.ManagedInstanceSystemImport
            });

        public static Role Administrator { get; } = new Role("sysadmin_role", "Administrator", "Certify Server Administrator",
            policies: new List<string> {
                     StandardPolicies.ManagementHubAdmin,
                     StandardPolicies.ManagedItemAdmin,
                     StandardPolicies.CertificateAuthorityAdmin,
                     StandardPolicies.AcmeAccountAdmin,
                     StandardPolicies.StoredCredentialAdmin,
                     StandardPolicies.ManagedChallengeAdmin,
                     StandardPolicies.AccessAdmin,
                     StandardPolicies.AccessTokenAdmin,
                     StandardPolicies.CertificateConsumer,
                     StandardPolicies.ManagedChallengeAdmin,
                     StandardPolicies.ManagedInstanceSystemExport,
                     StandardPolicies.ManagedInstanceSystemImport,
                     StandardPolicies.SystemUser
                    });

        public static Role CertificateManager { get; } = new Role("cert_manager_role", "Certificate Manager", "Can manage and administer all certificates",
            policies: new List<string> {
                StandardPolicies.ManagementHubReader,
                StandardPolicies.ManagedItemAdmin,
                StandardPolicies.StoredCredentialAdmin
                    });

        public static Role CertificateConsumer { get; } = new Role("cert_consumer_role", "Certificate Consumer", "User of a given certificate", policies: new List<string> { StandardPolicies.CertificateConsumer });

        public static Role StoredCredentialConsumer { get; } = new Role("storedcredential_consumer_role", "Stored Credential Fetch Consumer", "Can fetch a decrypted stored credential", policies: new List<string> { StandardPolicies.StoredCredentialConsumer });

        public static Role ManagedChallengeConsumer { get; } = new Role("managedchallenge_consumer_role", "Managed Challenge Consumer", "Can perform specific managed challenges", policies: new List<string> { StandardPolicies.ManagedChallengeConsumer });

        public static Role ManagedInstance { get; } = new Role("managedinstance_role", "Hub Managed Instance", "Can join the hub and be managed via the hub.", policies: new List<string> { StandardPolicies.ManagedInstance });
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
        public static string Role { get; } = "role";
        public static string AccessToken { get; } = "accesstoken";
        public static string Domain { get; } = "domain";
        public static string ManagedItem { get; } = "manageditem";
        public static string Certificate { get; } = "certificate";
        public static string StoredCredential { get; } = "storedcredential";
        public static string CertificateAuthority { get; } = "ca";
        public static string AcmeAccount { get; } = "acmeaccount";
        public static string ManagedChallenge { get; } = "managedchallenge";
        public static string ManagedInstance { get; } = "managedinstance";
        public static string Target { get; } = "target";
        public static string ChallengeProvider { get; } = "challengeprovider";
        public static string DeploymentTask { get; } = "deploymenttask";
    }

    public static class StandardResourceActions
    {
        public const string CertificateDownload = "certificate_download_action";
        public const string CertificateKeyDownload = "certificate_key_download_action";

        public const string ManagedItemRequest = "manageditem_requester_action";
        public const string ManagedItemAdd = "manageditem_add_action";
        public const string ManagedItemList = "manageditem_list_action";
        public const string ManagedItemUpdate = "manageditem_update_action";
        public const string ManagedItemDelete = "manageditem_delete_action";
        public const string ManagedItemTest = "manageditem_test_action";
        public const string ManagedItemRenew = "manageditem_renew_action";
        public const string ManagedItemTaskAdd = "manageditem_task_add_action";
        public const string ManagedItemTaskUpdate = "manageditem_task_update_action";
        public const string ManagedItemTaskDelete = "manageditem_task_delete_action";
        public const string ManagedItemLogView = "manageditem_log_view_action";

        public const string CertificateAuthorityAdd = "ca_add_action";
        public const string CertificateAuthorityUpdate = "ca_update_action";
        public const string CertificateAuthorityDelete = "ca_delete_action";
        public const string CertificateAuthorityList = "ca_list_action";

        public const string AcmeAccountAdd = "acmeaccount_add_action";
        public const string AcmeAccountUpdate = "acmeaccount_update_action";
        public const string AcmeAccountDelete = "acmeaccount_delete_action";
        public const string AcmeAccountList = "acmeaccount_list_action";

        public const string StoredCredentialAdd = "storedcredential_add_action";
        public const string StoredCredentialUpdate = "storedcredential_update_action";
        public const string StoredCredentialDelete = "storedcredential_delete_action";
        public const string StoredCredentialList = "storedcredential_list_action";
        public const string StoredCredentialDownload = "storedcredential_consumer_action";

        public const string SecurityPrincipleList = "securityprinciple_list_action";
        public const string SecurityPrincipleAdd = "securityprinciple_add_action";
        public const string SecurityPrincipleUpdate = "securityprinciple_update_action";
        public const string SecurityPrincipleUpdateAssignedRoles = "securityprinciple_update_assignedroles_action";
        public const string SecurityPrincipleDelete = "securityprinciple_delete_action";
        public const string SecurityPrinciplePasswordUpdate = "securityprinciple_password_update_action";
        public const string SecurityPrinciplePasswordValidate = "securityprinciple_password_validate_action";
        public const string SecurityPrincipleCheckAccess = "securityprinciple_access_check_action";

        public const string RoleList = "role_list_action";

        public const string ManagedChallengeList = "managedchallenge_list_action";
        public const string ManagedChallengeUpdate = "managedchallenge_update_action";
        public const string ManagedChallengeDelete = "managedchallenge_update_action";
        public const string ManagedChallengeRequest = "managedchallenge_request_action";
        public const string ManagedChallengeCleanup = "managedchallenge_cleanup_action";

        public const string ManagementHubInstancesList = "managementhub_instances_list_action";
        public const string ManagementHubInstanceJoin = "managementhub_instance_join_action";
        public const string ManagementHubInstanceDelete = "managementhub_instance_delete_action";
        public const string ManagementHubInstanceAdd = "managementhub_instance_add_action";
        public const string ManagementHubInstanceUpdate = "managementhub_instance_update_action";

        public const string ManagementHubInstanceExport = "managementhub_instance_export_action";
        public const string ManagementHubInstanceImport = "managementhub_instance_import_action";

        public const string AccessTokenList = "accesstoken_list_action";
        public const string AccessTokenAdd = "accesstoken_add_action";
        public const string AccessTokenUpdate = "accesstoken_update_action";
        public const string AccessTokenDelete = "accesstoken_delete_action";

        public const string SystemGeneralAction = "system_general_action";

        public const string SystemStatusList = "system_status_list_action";
        public const string SystemServiceConfigList = "system_serviceconfig_list_action";
        public const string SystemCoreSettingsList = "system_coresettings_list_action";
        public const string SystemServiceConfigUpdate = "system_serviceconfig_update_action";
        public const string SystemCoreSettingsUpdate = "system_coresettings_update_action";

        public const string TargetIPAddressesList = "target_ipaddresses_list_action";
        public const string TargetTypesList = "target_types_list_action";
        public const string TargetServiceItemsList = "target_serviceitems_list_action";
        public const string TargetServiceItemIdentifiersList = "target_serviceitemidentifiers_list_action";

        public const string ChallengeProviderList = "challengeprovider_list_action";
        public const string ChallengeProviderDnsZonesList = "challengeprovider_dnszones_list_action";

        public const string DeploymentTaskExecute = "deploymenttask_execute_action";
        public const string DeploymentTaskListProviders = "deploymenttask_list_providers_action";

    }

    public class StandardPolicies
    {
        public const string AccessAdmin = "access_admin_policy";
        public const string AccessTokenAdmin = "accesstoken_admin_policy";
        public const string ManagedItemAdmin = "manageditem_admin_policy";
        public const string CertificateConsumer = "certificate_consumer_policy";
        public const string CertificateAuthorityAdmin = "ca_admin_policy";
        public const string AcmeAccountAdmin = "acmeaccount_admin_policy";
        public const string StoredCredentialAdmin = "storedcredential_admin_policy";
        public const string StoredCredentialConsumer = "storedcredential_consumer_policy";
        public const string ManagedChallengeConsumer = "managedchallenge_consumer_policy";
        public const string ManagedChallengeAdmin = "managedchallenge_admin_policy";
        public const string ManagementHubAdmin = "managementhub_admin_policy";
        public const string ManagementHubReader = "managementhub_reader_policy";
        public const string ManagedInstance = "managementhub_managedinstance_policy";
        public const string ManagedInstanceSystemImport = "system_import_policy";
        public const string ManagedInstanceSystemExport = "system_export_policy";
        public const string SystemUser = "system_user_policy";

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
                StandardRoles.ManagedChallengeConsumer,
                StandardRoles.ManagedInstance,
                StandardRoles.BackupOperator
            };
        }

        public static List<ResourceAction> GetStandardResourceActions()
        {
            return new List<ResourceAction> {

                new(StandardResourceActions.CertificateDownload, "Certificate Download", ResourceTypes.Certificate),
                new(StandardResourceActions.CertificateKeyDownload, "Certificate Private Key Download", ResourceTypes.Certificate),

                new(StandardResourceActions.CertificateAuthorityAdd, "Add New Certificate Authority", ResourceTypes.CertificateAuthority),
                new(StandardResourceActions.CertificateAuthorityUpdate, "Update Certificate Authority", ResourceTypes.CertificateAuthority),
                new(StandardResourceActions.CertificateAuthorityDelete, "Delete Certificate Authority", ResourceTypes.CertificateAuthority),
                new(StandardResourceActions.CertificateAuthorityList, "List Certificate Authority", ResourceTypes.CertificateAuthority),

                new(StandardResourceActions.AcmeAccountAdd, "Add New ACME Account", ResourceTypes.AcmeAccount),
                new(StandardResourceActions.AcmeAccountUpdate, "Update ACME Account", ResourceTypes.AcmeAccount),
                new(StandardResourceActions.AcmeAccountDelete, "Delete ACME Account", ResourceTypes.AcmeAccount),
                new(StandardResourceActions.AcmeAccountList, "List ACME Accounts", ResourceTypes.AcmeAccount),

                new(StandardResourceActions.StoredCredentialAdd, "Add New Stored Credential", ResourceTypes.StoredCredential),
                new(StandardResourceActions.StoredCredentialUpdate, "Update Stored Credential", ResourceTypes.StoredCredential),
                new(StandardResourceActions.StoredCredentialDelete, "Delete Stored Credential", ResourceTypes.StoredCredential),
                new(StandardResourceActions.StoredCredentialList, "List Stored Credentials", ResourceTypes.StoredCredential),
                new(StandardResourceActions.StoredCredentialDownload, "Fetch Decrypted Stored Credential", ResourceTypes.StoredCredential),

                new(StandardResourceActions.SecurityPrincipleList, "List Security Principles", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrincipleAdd, "Add New Security Principle", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrincipleUpdate,"Update Security Principles", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrincipleUpdateAssignedRoles,"Update Security Principle Assigned Roles", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrinciplePasswordUpdate, "Update Security Principle Passwords", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrincipleDelete, "Delete Security Principle", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrincipleCheckAccess, "Check Security Principle Access", ResourceTypes.SecurityPrinciple),
                new(StandardResourceActions.SecurityPrinciplePasswordValidate, "Validate Security Principle Passwords", ResourceTypes.SecurityPrinciple),

                new(StandardResourceActions.AccessTokenAdd, "Add Access Token", ResourceTypes.AccessToken),
                new(StandardResourceActions.AccessTokenDelete, "Delete Access Token", ResourceTypes.AccessToken),
                new(StandardResourceActions.AccessTokenList, "List Access Tokens", ResourceTypes.AccessToken),
                new(StandardResourceActions.AccessTokenUpdate, "Update Access Token", ResourceTypes.AccessToken),

                new(StandardResourceActions.RoleList, "List Roles", ResourceTypes.Role),

                new(StandardResourceActions.ManagedItemRequest, "Request New Managed Items", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedItemList, "List Managed Items", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemAdd, "Add Managed Items", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemUpdate, "Update Managed Items", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemDelete, "Delete Managed Items", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedItemTest, "Test Managed Item Renewal Checks", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemRequest, "Request Managed Items", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemRenew, "Renew Managed Items", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedItemTaskAdd, "Add Managed Item Tasks", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemTaskUpdate, "Update Managed Item Tasks", ResourceTypes.ManagedItem),
                new(StandardResourceActions.ManagedItemTaskDelete, "Delete Managed Item Tasks", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedItemLogView, "View/Download Managed Item Log", ResourceTypes.ManagedItem),

                new(StandardResourceActions.ManagedChallengeList, "List managed challenges", ResourceTypes.ManagedChallenge),
                new(StandardResourceActions.ManagedChallengeUpdate, "Update managed challenge", ResourceTypes.ManagedChallenge),
                new(StandardResourceActions.ManagedChallengeDelete, "Delete managed challenge", ResourceTypes.ManagedChallenge),
                new(StandardResourceActions.ManagedChallengeRequest, "Request to perform a managed challenge response", ResourceTypes.ManagedChallenge),
                new(StandardResourceActions.ManagedChallengeCleanup, "Cleanup managed challenges", ResourceTypes.ManagedChallenge),

                new(StandardResourceActions.ManagementHubInstancesList, "List managed instances", ResourceTypes.ManagedInstance),
                new(StandardResourceActions.ManagementHubInstanceJoin, "Join management hub as a managed instance", ResourceTypes.ManagedInstance),
                new(StandardResourceActions.ManagementHubInstanceDelete, "Delete managed instance from the hub", ResourceTypes.ManagedInstance),
                new(StandardResourceActions.ManagementHubInstanceAdd, "Add managed instance details to the hub", ResourceTypes.ManagedInstance),
                new(StandardResourceActions.ManagementHubInstanceUpdate, "Update managed instance detail in the hub", ResourceTypes.ManagedInstance),

                new(StandardResourceActions.ManagementHubInstanceExport, "Export system configuration", ResourceTypes.ManagedInstance),
                new(StandardResourceActions.ManagementHubInstanceImport, "Import system configuration", ResourceTypes.ManagedInstance),

                new(StandardResourceActions.SystemStatusList, "List system status", ResourceTypes.System),
                new(StandardResourceActions.SystemServiceConfigList, "List system service configuration", ResourceTypes.System),
                new(StandardResourceActions.SystemCoreSettingsList, "List system core settings", ResourceTypes.System),
                new(StandardResourceActions.SystemServiceConfigUpdate, "Update system service configuration", ResourceTypes.System),
                new(StandardResourceActions.SystemCoreSettingsUpdate, "Update system core settings", ResourceTypes.System),

                new(StandardResourceActions.TargetIPAddressesList, "List target IP addresses", ResourceTypes.Target),
                new(StandardResourceActions.TargetTypesList, "List target types", ResourceTypes.Target),
                new(StandardResourceActions.TargetServiceItemsList, "List target service items", ResourceTypes.Target),
                new(StandardResourceActions.TargetServiceItemIdentifiersList, "List target service item identifiers", ResourceTypes.Target),

                new(StandardResourceActions.ChallengeProviderList, "List challenge providers", ResourceTypes.ChallengeProvider),
                new(StandardResourceActions.ChallengeProviderDnsZonesList, "List challenge provider DNS zones", ResourceTypes.ChallengeProvider),

                new(StandardResourceActions.DeploymentTaskExecute, "Execute deployment task", ResourceTypes.DeploymentTask),
                new(StandardResourceActions.DeploymentTaskListProviders, "List deployment task providers", ResourceTypes.DeploymentTask)

            };
        }

        public static List<ResourcePolicy> GetStandardPolicies()
        {
            return new List<ResourcePolicy> {
                new() {
                    Id = StandardPolicies.ManagedItemAdmin,
                    Title = "Managed Item Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = new List<string> {
                        StandardResourceActions.ManagedItemList,
                        StandardResourceActions.ManagedItemAdd,
                        StandardResourceActions.ManagedItemUpdate,
                        StandardResourceActions.ManagedItemDelete,
                        StandardResourceActions.ManagedItemTest,
                        StandardResourceActions.ManagedItemRequest,
                        StandardResourceActions.ManagedItemRenew,
                        StandardResourceActions.ManagedItemTaskAdd,
                        StandardResourceActions.ManagedItemTaskUpdate,
                        StandardResourceActions.ManagedItemTaskDelete,
                        StandardResourceActions.ManagedItemLogView,
                        StandardResourceActions.TargetIPAddressesList,
                        StandardResourceActions.TargetServiceItemIdentifiersList,
                        StandardResourceActions.TargetServiceItemsList,
                        StandardResourceActions.TargetTypesList,
                        StandardResourceActions.ChallengeProviderList,
                        StandardResourceActions.ChallengeProviderDnsZonesList,
                        StandardResourceActions.DeploymentTaskExecute,
                        StandardResourceActions.DeploymentTaskListProviders
                    }
                },
                new() {
                    Id = StandardPolicies.AccessAdmin,
                    Title = "Access Control Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = new List<string> {
                        StandardResourceActions.SecurityPrincipleList,
                        StandardResourceActions.SecurityPrincipleAdd,
                        StandardResourceActions.SecurityPrincipleUpdate,
                        StandardResourceActions.SecurityPrincipleDelete,
                        StandardResourceActions.SecurityPrinciplePasswordUpdate,
                        StandardResourceActions.SecurityPrincipleUpdateAssignedRoles

                    }
                },
                new() {
                     Id = StandardPolicies.AccessTokenAdmin,
                     Title = "Access Token Administration",
                     SecurityPermissionType = SecurityPermissionType.ALLOW,
                     ResourceActions = new List<string> {
                         StandardResourceActions.AccessTokenList,
                         StandardResourceActions.AccessTokenAdd,
                         StandardResourceActions.AccessTokenDelete,
                         StandardResourceActions.AccessTokenUpdate,
                     }
                 },
                new() {
                    Id = StandardPolicies.CertificateConsumer,
                    Title = "Consume Certificates",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = new List<string> {
                        StandardResourceActions.CertificateDownload,
                        StandardResourceActions.CertificateKeyDownload
                    }
                },
                new() {
                    Id = StandardPolicies.CertificateAuthorityAdmin,
                    Title = "Certificate Authority Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = new List<string> {
                        StandardResourceActions.CertificateAuthorityAdd,
                        StandardResourceActions.CertificateAuthorityUpdate,
                        StandardResourceActions.CertificateAuthorityDelete,
                        StandardResourceActions.CertificateAuthorityList
                    }
                },
                new() {
                    Id = StandardPolicies.AcmeAccountAdmin,
                    Title = "ACME Account Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = new List<string> {
                        StandardResourceActions.AcmeAccountList,
                        StandardResourceActions.AcmeAccountAdd,
                        StandardResourceActions.AcmeAccountUpdate,
                        StandardResourceActions.AcmeAccountDelete
                    }
                },
                new() {
                    Id = StandardPolicies.StoredCredentialAdmin,
                    Title = "Stored Credential Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = new List<string> {
                        StandardResourceActions.StoredCredentialList,
                        StandardResourceActions.StoredCredentialAdd,
                        StandardResourceActions.StoredCredentialUpdate,
                        StandardResourceActions.StoredCredentialDelete
                    }
                },
                new() {
                    Id = StandardPolicies.StoredCredentialConsumer,
                    Title = "Stored Credential Consumer",
                    Description = "Provides access to fetch a decrypted stored credential.",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    IsResourceSpecific = true,
                    ResourceActions = new List<string> {
                        StandardResourceActions.StoredCredentialDownload
                    }
                },
                new() {
                    Id = StandardPolicies.ManagedChallengeAdmin,
                    Title = "Managed Challenge Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = new List<string> {
                        StandardResourceActions.ManagedChallengeList,
                        StandardResourceActions.ManagedChallengeUpdate,
                        StandardResourceActions.ManagedChallengeDelete
                    }
                },
                new() {
                    Id = StandardPolicies.ManagedChallengeConsumer,
                    Title = "Managed Challenge Consumer",
                    Description = "Allows consumer to request that a managed challenge be performed.",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    IsResourceSpecific = true,
                    ResourceActions = new List<string> {
                        StandardResourceActions.ManagedChallengeRequest,
                        StandardResourceActions.ManagedChallengeCleanup
                    }
                },
                new() {
                    Id = StandardPolicies.ManagementHubAdmin,
                    Title = "Management Hub Admin",
                    Description = "Administer management hub.",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    IsResourceSpecific = true,
                    ResourceActions = new List<string> {
                        StandardResourceActions.ManagementHubInstancesList,
                        StandardResourceActions.ManagementHubInstanceAdd,
                        StandardResourceActions.ManagementHubInstanceUpdate,
                        StandardResourceActions.ManagementHubInstanceDelete,
                        StandardResourceActions.SystemStatusList,
                        StandardResourceActions.SystemCoreSettingsList,
                        StandardResourceActions.SystemCoreSettingsUpdate,
                        StandardResourceActions.SystemServiceConfigList,
                        StandardResourceActions.SystemServiceConfigUpdate,

                    }
                },
                new() {
                    Id = StandardPolicies.ManagementHubReader,
                    Title = "Management Hub Reader",
                    Description = "View management hub.",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    IsResourceSpecific = true,
                    ResourceActions = new List<string> {
                        StandardResourceActions.ManagementHubInstancesList
                    }
                },
                new() {
                     Id = StandardPolicies.ManagedInstance,
                     Title = "Management Hub Managed Instance",
                     Description = "Join management hub and allow to be managed by hub.",
                     SecurityPermissionType = SecurityPermissionType.ALLOW,
                     IsResourceSpecific = true,
                     ResourceActions = new List<string> {
                         StandardResourceActions.ManagementHubInstanceJoin
                     }
                 },
                  new() {
                 Id = StandardPolicies.ManagedInstanceSystemImport,
                 Title = "Instance Configuration Import",
                 Description = "Import system configuration and apply to a target instance",
                 SecurityPermissionType = SecurityPermissionType.ALLOW,
                 IsResourceSpecific = true,
                 ResourceActions = new List<string> {
                     StandardResourceActions.ManagementHubInstanceImport
                 }
             },
              new() {
                 Id = StandardPolicies.ManagedInstanceSystemExport,
                 Title = "Instance Configuration Export",
                 Description = "Export system configuration for a target instance",
                 SecurityPermissionType = SecurityPermissionType.ALLOW,
                 IsResourceSpecific = true,
                 ResourceActions = new List<string> {
                     StandardResourceActions.ManagementHubInstanceExport
                 }
              },
                new() {
                 Id = StandardPolicies.SystemUser,
                 Title = "System User",
                 Description = "Perform general system use actions",
                 SecurityPermissionType = SecurityPermissionType.ALLOW,
                 IsResourceSpecific = true,
                 ResourceActions = new List<string> {
                     StandardResourceActions.SecurityPrincipleCheckAccess,
                     StandardResourceActions.SecurityPrinciplePasswordValidate,
                     StandardResourceActions.RoleList,
                 }
              }
            };
        }
    }

    public static class AccessControlConfig
    {
        /// <summary>
        /// Add/update standard system roles, policies and resource actions
        /// </summary>
        /// <param name="access"></param>
        /// <returns></returns>
        public static async Task UpdateStandardAccessConfig(IAccessControl access)
        {
            // setup roles with policies

            var adminSvcPrinciple = "admin_01";

            var actions = Policies.GetStandardResourceActions();

            foreach (var action in actions)
            {
                await access.AddResourceAction(adminSvcPrinciple, action, bypassIntegrityCheck: true);
            }

            // setup policies with actions

            var policies = Policies.GetStandardPolicies();

            // add policies to store
            foreach (var r in policies)
            {
                _ = await access.AddResourcePolicy(adminSvcPrinciple, r, bypassIntegrityCheck: true);
            }

            // setup roles with policies
            var roles = Policies.GetStandardRoles();

            foreach (var r in roles)
            {
                // add roles and policy assignments to store
                await access.AddRole(adminSvcPrinciple, r, bypassIntegrityCheck: true);
            }
        }

        public static async Task ConfigureStandardUsersAndRoles(IAccessControl access, ICredentialsManager creds)
        {
            // setup roles with policies
            await UpdateStandardAccessConfig(access);

            // setup standard security principles

            // admin user
            var adminSpId = "admin_01";
            var managedInstanceSpId = "managedinstance_sp_01";

            var users = await access.GetSecurityPrinciples(adminSpId);

            // add admin user if not already present
            if (!users.Any(u => u.Id == adminSpId))
            {
                var adminSp = new SecurityPrinciple
                {
                    Id = adminSpId,
                    Description = "Primary default admin",
                    PrincipleType = SecurityPrincipleType.User,
                    Username = Environment.GetEnvironmentVariable("CERTIFY_ADMIN_DEFAULTUSERNAME") ?? "admin",
                    Password = Environment.GetEnvironmentVariable("CERTIFY_ADMIN_DEFAULTPWD") ?? "changeme!",
                    Provider = StandardIdentityProviders.INTERNAL,
                    IsBuiltIn = true
                };

                await access.AddSecurityPrinciple(adminSp.Id, adminSp, bypassIntegrityCheck: true);
            }
            // get assigned roles for admin and update any missing roles
            var assignedRolesForAdmin = await access.GetAssignedRoles(adminSpId, adminSpId);

            // assign admin role to admin security principle
            var toBeAssignedRoles = new List<AssignedRole> {
                     // administrator
                     new AssignedRole{
                         Id= Guid.NewGuid().ToString(),
                         RoleId=StandardRoles.Administrator.Id,
                         SecurityPrincipleId=adminSpId
                     }
                };

            foreach (var r in toBeAssignedRoles)
            {
                if (assignedRolesForAdmin?.Any(a => a.RoleId == r.RoleId) != true)
                {
                    // add roles and policy assignments to store
                    await access.AddAssignedRole(adminSpId, r, bypassIntegrityCheck: true);
                }
            }

            // add managed instance service principle if not already present
            if (!users.Any(u => u.Id == managedInstanceSpId))
            {
                var managedInstanceServicePrinciple = new SecurityPrinciple
                {
                    Id = managedInstanceSpId,
                    Title = "Managed Instances Service Principle",
                    PrincipleType = SecurityPrincipleType.Application,
                    Provider = StandardIdentityProviders.INTERNAL,
                    IsBuiltIn = true
                };

                await access.AddSecurityPrinciple(adminSpId, managedInstanceServicePrinciple, bypassIntegrityCheck: true);

                // assign managed instance role to  security principle
                var assignedRoles = new List<AssignedRole> {

                    new AssignedRole{
                        Id= Guid.NewGuid().ToString(),
                        RoleId=StandardRoles.ManagedInstance.Id,
                        SecurityPrincipleId=managedInstanceSpId
                    }
                };

                foreach (var r in assignedRoles)
                {
                    // add roles and policy assignments to store
                    await access.AddAssignedRole(adminSpId, r, bypassIntegrityCheck: true);
                }

                // assign an API token for hub managed instances scoped to the managed instance role
                var assignedApiAccessToken = new AssignedAccessToken
                {
                    Id = Guid.NewGuid().ToString(),
                    SecurityPrincipleId = managedInstanceSpId,
                    Title = "Managed Instance Hub Joining Key",
                    AccessTokens = new List<AccessToken> {
                        new AccessToken {
                            ClientId = managedInstanceSpId,
                            Description = "System Generated",
                            Secret = Guid.NewGuid().ToString().ToLowerInvariant(),
                            TokenType = AccessTokenTypes.Simple,
                            DateCreated = DateTime.UtcNow
                        }
                    },
                    ScopedAssignedRoles = new List<string> {
                        // scope assigned role is the id for AssignedRole (not the role id itself)
                        assignedRoles.First(a=>a.RoleId==StandardRoles.ManagedInstance.Id).Id
                    },
                };

                await access.AddAssignedAccessToken(adminSpId, assignedApiAccessToken);
            }

            // if we don't have a stored credential as a client secret for the managed instance to join it's own hub, create one
            // direct instances don't really need this, but remote backends do so they can join back to their own hub.
            var existingJoiningKey = await creds.GetUnlockedCredential("_ManagementHubJoiningKey");
            if (existingJoiningKey == null)
            {
                var assignedTokens = await access.GetAssignedAccessTokens(contextUserId: adminSpId);
                var token = assignedTokens.First(t => t.Title == "Managed Instance Hub Joining Key")?.AccessTokens?.First();

                if (token != null)
                {
                    var clientSecret = new ClientSecret { ClientId = token.ClientId, Secret = token.Secret };
                    await creds.Update(new Config.StoredCredential
                    {
                        StorageKey = "_ManagementHubJoiningKey",
                        ProviderType = StandardAuthTypes.STANDARD_AUTH_MGMTHUB,
                        Title = "Management Hub Joining Key",
                        Secret = System.Text.Json.JsonSerializer.Serialize(clientSecret)
                    });
                }
            }
        }
    }
}
