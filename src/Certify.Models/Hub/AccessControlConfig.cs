using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Management;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Well known security principal identifiers which are implied rather than stored in the access control data store.
    /// </summary>
    public static class StandardSecurityPrincipals
    {
        /// <summary>
        /// The implied internal system principal, used for trusted in-process/service calls (e.g. ACME endpoints and
        /// internal hub services) which have no associated stored security principal. This context may evaluate access
        /// on behalf of another principal but is not itself granted blanket authorization.
        /// </summary>
        public const string System = "system";
    }

    public class StandardRoles
    {
        public static Role BackupOperator { get; } = new Role("backup_operator_role", "Backup Operator", "Can perform import and export operations",
            policies: [
                StandardPolicies.ManagedInstanceSystemExport,
                StandardPolicies.ManagedInstanceSystemImport,
                StandardPolicies.SystemUser
            ]);

        public static Role Administrator { get; } = new Role("sysadmin_role", "Administrator", "Certify Server Administrator",
            policies: [
                     StandardPolicies.ManagementHubAdmin,
                     StandardPolicies.ManagedItemAdmin,
                     StandardPolicies.CertificateAuthorityAdmin,
                     StandardPolicies.AcmeAccountAdmin,
                     StandardPolicies.StoredCredentialAdmin,
                     StandardPolicies.ManagedChallengeAdmin,
                     StandardPolicies.AccessAdmin,
                     StandardPolicies.OidcAdmin,
                     StandardPolicies.AccessTokenAdmin,
                     StandardPolicies.CertificateConsumer,
                     StandardPolicies.ManagedInstanceSystemExport,
                     StandardPolicies.ManagedInstanceSystemImport,
                     StandardPolicies.TagAdmin,
                     StandardPolicies.ManagedLicenseAdmin,
                     StandardPolicies.SystemUser
                    ]);

        public static Role CertificateManager { get; } = new Role("cert_manager_role", "Certificate Manager", "Can manage and administer all certificates",
            policies: [
                StandardPolicies.ManagementHubReader,
                StandardPolicies.ManagedItemAdmin,
                StandardPolicies.StoredCredentialAdmin,
                StandardPolicies.CertificateConsumer,
                StandardPolicies.SystemUser
                    ]);
        public static Role HubViewer { get; } = new Role("hub_viewer_role", "Hub Viewer", "Can view all hub managed certificates and summary information",
            policies: [
                StandardPolicies.ManagementHubReader,
                StandardPolicies.SystemUser
            ]);

        public static Role CertificateConsumer { get; } = new Role("cert_consumer_role", "Certificate Consumer", "User of a given certificate", policies: [StandardPolicies.CertificateConsumer]);

        public static Role StoredCredentialConsumer { get; } = new Role("storedcredential_consumer_role", "Stored Credential Fetch Consumer", "Can fetch a decrypted stored credential", policies: [StandardPolicies.StoredCredentialConsumer]);
        public static Role ManagedChallengeAdmin { get; } = new Role("managedchallenge_admin_role", "Managed Challenge Admin", "Can administer managed challenge configurations", policies: [StandardPolicies.ManagedChallengeAdmin]);
        public static Role ManagedChallengeConsumer { get; } = new Role("managedchallenge_consumer_role", "Managed Challenge Consumer", "Can perform specific managed challenges", policies: [StandardPolicies.ManagedChallengeConsumer]);

        public static Role ManagedInstance { get; } = new Role("managedinstance_role", "Hub Managed Instance", "Can join the hub and be managed via the hub.", policies: [StandardPolicies.ManagedInstance]);
        public static Role ManagedAcmeConsumer { get; } = new Role("managedacme_consumer_role", "Managed ACME Consumer", "Can use managed ACME services via the hub.", policies: [StandardPolicies.ManagedAcmeConsumer]);
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
        public const string System = "system";
        public const string SecurityPrincipal = "securityprincipal";
        public const string Role = "role";
        public const string AccessToken = "accesstoken";
        public const string Domain = "domain";
        public const string ManagedItem = "manageditem";
        public const string Certificate = "certificate";
        public const string StoredCredential = "storedcredential";
        public const string CertificateAuthority = "ca";
        public const string AcmeAccount = "acmeaccount";
        public const string ManagedChallenge = "managedchallenge";
        public const string ManagedAcme = "managedacme";
        public const string ManagedInstance = "managedinstance";
        public const string ManagedLicense = "managedlicense";
        public const string OidcProvider = "oidcprovider";
        public const string Target = "target";
        public const string ChallengeProvider = "challengeprovider";
        public const string DeploymentTask = "deploymenttask";
        public const string Tag = "tag";
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
        public const string StoredCredentialReadSecret = "storedcredential_consumer_action";

        public const string SecurityPrincipalList = "securityprincipal_list_action";
        public const string SecurityPrincipalAdd = "securityprincipal_add_action";
        public const string SecurityPrincipalUpdate = "securityprincipal_update_action";
        public const string SecurityPrincipalUpdateAssignedRoles = "securityprincipal_update_assignedroles_action";
        public const string SecurityPrincipalDelete = "securityprincipal_delete_action";
        public const string SecurityPrincipalPasswordUpdate = "securityprincipal_password_update_action";
        public const string SecurityPrincipalPasswordValidate = "securityprincipal_password_validate_action";
        public const string SecurityPrincipalCheckAccess = "securityprincipal_access_check_action";

        public const string RoleList = "role_list_action";

        public const string ManagedChallengeList = "managedchallenge_list_action";
        public const string ManagedChallengeUpdate = "managedchallenge_update_action";
        public const string ManagedChallengeDelete = "managedchallenge_delete_action";
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

        public const string SystemStatusList = "system_status_list_action";
        public const string SystemLogList = "system_log_list_action";
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

        public const string TagAdd = "managementhub_tag_add_action";
        public const string TagDelete = "managementhub_tag_delete_action";
        public const string TagUpdate = "managementhub_tag_update_action";
        public const string TagList = "managementhub_tag_list_action";

        public const string ManagedLicenseList = "managedlicense_list_action";
        public const string ManagedLicenseAdd = "managedlicense_add_action";
        public const string ManagedLicenseUpdate = "managedlicense_update_action";
        public const string ManagedLicenseDelete = "managedlicense_delete_action";
        public const string ManagedLicenseActivate = "managedlicense_activate_action";
        public const string ManagedLicenseDeactivate = "managedlicense_deactivate_action";
        public const string ManagedLicenseStatus = "managedlicense_status_action";

        public const string ManagedAcmePerformOrder = "managedacme_order_action";

        public const string OidcProviderList = "oidcprovider_list_action";
        public const string OidcProviderAdd = "oidcprovider_add_action";
        public const string OidcProviderUpdate = "oidcprovider_update_action";
        public const string OidcProviderDelete = "oidcprovider_delete_action";

    }

    public class StandardPolicies
    {
        public const string AccessAdmin = "access_admin_policy";
        public const string OidcAdmin = "oidc_admin_policy";
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
        public const string ManagedLicenseAdmin = "managedlicense_admin_policy";
        public const string ManagedAcmeConsumer = "managedacme_consumer_policy";
        public const string SystemUser = "system_user_policy";
        public const string TagAdmin = "tag_admin_policy";

    }

    public static class Policies
    {
        public static List<Role> GetStandardRoles()
        {
            return
            [
                StandardRoles.Administrator,
                StandardRoles.CertificateManager,
                StandardRoles.CertificateConsumer,
                StandardRoles.StoredCredentialConsumer,
                StandardRoles.ManagedChallengeAdmin,
                StandardRoles.ManagedChallengeConsumer,
                StandardRoles.ManagedAcmeConsumer,
                StandardRoles.HubViewer,
                StandardRoles.ManagedInstance,
                StandardRoles.BackupOperator
            ];
        }

        public static List<ResourceAction> GetStandardResourceActions()
        {
            return [

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
                new(StandardResourceActions.StoredCredentialReadSecret, "Fetch Decrypted Stored Credential", ResourceTypes.StoredCredential),

                new(StandardResourceActions.SecurityPrincipalList, "List Security Principals", ResourceTypes.SecurityPrincipal),
                new(StandardResourceActions.SecurityPrincipalAdd, "Add New Security Principal", ResourceTypes.SecurityPrincipal),
                new(StandardResourceActions.SecurityPrincipalUpdate,"Update Security Principals", ResourceTypes.SecurityPrincipal),
                new(StandardResourceActions.SecurityPrincipalUpdateAssignedRoles,"Update Security Principal Assigned Roles", ResourceTypes.SecurityPrincipal),
                new(StandardResourceActions.SecurityPrincipalPasswordUpdate, "Update Security Principal Passwords", ResourceTypes.SecurityPrincipal),
                new(StandardResourceActions.SecurityPrincipalDelete, "Delete Security Principal", ResourceTypes.SecurityPrincipal),
                new(StandardResourceActions.SecurityPrincipalCheckAccess, "Check Security Principal Access", ResourceTypes.SecurityPrincipal),
                new(StandardResourceActions.SecurityPrincipalPasswordValidate, "Validate Security Principal Passwords", ResourceTypes.SecurityPrincipal),

                new(StandardResourceActions.AccessTokenAdd, "Add Access Token", ResourceTypes.AccessToken),
                new(StandardResourceActions.AccessTokenDelete, "Delete Access Token", ResourceTypes.AccessToken),
                new(StandardResourceActions.AccessTokenList, "List Access Tokens", ResourceTypes.AccessToken),
                new(StandardResourceActions.AccessTokenUpdate, "Update Access Token", ResourceTypes.AccessToken),

                new(StandardResourceActions.RoleList, "List Roles", ResourceTypes.Role),

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
                new(StandardResourceActions.SystemLogList, "List and download system logs", ResourceTypes.System),
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
                new(StandardResourceActions.DeploymentTaskListProviders, "List deployment task providers", ResourceTypes.DeploymentTask),

                new(StandardResourceActions.TagList, "List item tags", ResourceTypes.Tag),
                new(StandardResourceActions.TagAdd, "Add item tags", ResourceTypes.Tag),
                new(StandardResourceActions.TagUpdate, "Update item tags", ResourceTypes.Tag),
                new(StandardResourceActions.TagDelete, "Delete item tags", ResourceTypes.Tag),

                new(StandardResourceActions.ManagedLicenseList, "List managed licenses", ResourceTypes.ManagedLicense),
                new(StandardResourceActions.ManagedLicenseAdd, "Add managed license", ResourceTypes.ManagedLicense),
                new(StandardResourceActions.ManagedLicenseUpdate, "Update managed license", ResourceTypes.ManagedLicense),
                new(StandardResourceActions.ManagedLicenseDelete, "Delete managed license", ResourceTypes.ManagedLicense),
                new(StandardResourceActions.ManagedLicenseActivate, "Apply managed license to an instance", ResourceTypes.ManagedLicense),
                new(StandardResourceActions.ManagedLicenseDeactivate, "Remove managed license from an instance", ResourceTypes.ManagedLicense),
                new(StandardResourceActions.ManagedLicenseStatus, "Get status for a managed license", ResourceTypes.ManagedLicense),

                new(StandardResourceActions.ManagedAcmePerformOrder, "Perform managed acme order", ResourceTypes.ManagedAcme),

                new(StandardResourceActions.OidcProviderList, "List Oidc Provider licenses", ResourceTypes.OidcProvider),
                new(StandardResourceActions.OidcProviderAdd, "Add Oidc Provider", ResourceTypes.OidcProvider),
                new(StandardResourceActions.OidcProviderUpdate, "Update Oidc Provider", ResourceTypes.OidcProvider),
                new(StandardResourceActions.OidcProviderDelete, "Delete Oidc Provider", ResourceTypes.OidcProvider),

            ];
        }

        /// <summary>
        /// Maps an access control resource type to the corresponding taggable item type, where one exists.
        /// Returns null for resource types which are not tag scoped (e.g. system, role, accesstoken).
        /// </summary>
        public static string? GetTaggedItemTypeForResourceType(string? resourceType)
        {
            if (string.IsNullOrWhiteSpace(resourceType))
            {
                return null;
            }

            switch (resourceType.ToLowerInvariant())
            {
                case ResourceTypes.ManagedItem:
                case ResourceTypes.Certificate:
                    return TaggedItemTypes.ManagedCertificate;
                case ResourceTypes.ManagedInstance:
                    return TaggedItemTypes.ManagedInstance;
                case ResourceTypes.StoredCredential:
                    return TaggedItemTypes.StoredCredential;
                case ResourceTypes.DeploymentTask:
                    return TaggedItemTypes.DeploymentTask;
                case ResourceTypes.ManagedChallenge:
                case ResourceTypes.ManagedAcme:
                    return TaggedItemTypes.ManagedChallenge;
                case ResourceTypes.SecurityPrincipal:
                    return TaggedItemTypes.SecurityPrincipal;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Determine which taggable resource types a set of roles can actually act upon, so that tag scope
        /// can be previewed against only the resources the role really grants access to. For example a
        /// Managed ACME Consumer only grants managed challenge access, not managed certificate listing.
        /// </summary>
        public static List<string> GetTaggedItemTypesForRoles(IEnumerable<string>? roleIds)
        {
            var result = new List<string>();

            if (roleIds == null)
            {
                return result;
            }

            var roles = GetStandardRoles();
            var policies = GetStandardPolicies();
            var actions = GetStandardResourceActions();

            foreach (var roleId in roleIds)
            {
                var role = roles.FirstOrDefault(r => string.Equals(r.Id, roleId, StringComparison.OrdinalIgnoreCase));

                if (role == null)
                {
                    continue;
                }

                foreach (var policy in policies.Where(p => role.Policies.Contains(p.Id) && p.SecurityPermissionType == SecurityPermissionType.ALLOW))
                {
                    foreach (var actionId in policy.ResourceActions)
                    {
                        var action = actions.FirstOrDefault(a => a.Id == actionId);
                        var taggedItemType = GetTaggedItemTypeForResourceType(action?.ResourceType);

                        if (taggedItemType != null && !result.Contains(taggedItemType))
                        {
                            result.Add(taggedItemType);
                        }
                    }
                }
            }

            return result;
        }

        public static List<ResourcePolicy> GetStandardPolicies()
        {
            return [
                new() {
                    Id = StandardPolicies.ManagedItemAdmin,
                    Title = "Managed Item Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = [
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
                    ]
                },
                new() {
                    Id = StandardPolicies.AccessAdmin,
                    Title = "Access Control Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = [
                        StandardResourceActions.SecurityPrincipalList,
                        StandardResourceActions.SecurityPrincipalAdd,
                        StandardResourceActions.SecurityPrincipalUpdate,
                        StandardResourceActions.SecurityPrincipalDelete,
                        StandardResourceActions.SecurityPrincipalPasswordUpdate,
                        StandardResourceActions.SecurityPrincipalUpdateAssignedRoles

                    ]
                },
                 new() {
                     Id = StandardPolicies.OidcAdmin,
                     Title = "Oidc Provider Administration",
                     SecurityPermissionType = SecurityPermissionType.ALLOW,
                     ResourceActions = [
                         StandardResourceActions.OidcProviderList,
                         StandardResourceActions.OidcProviderAdd,
                         StandardResourceActions.OidcProviderUpdate,
                         StandardResourceActions.OidcProviderDelete,
                     ]
                 },
                new() {
                     Id = StandardPolicies.AccessTokenAdmin,
                     Title = "Access Token Administration",
                     SecurityPermissionType = SecurityPermissionType.ALLOW,
                     ResourceActions = [
                         StandardResourceActions.AccessTokenList,
                         StandardResourceActions.AccessTokenAdd,
                         StandardResourceActions.AccessTokenDelete,
                         StandardResourceActions.AccessTokenUpdate,
                     ]
                 },
                new() {
                    Id = StandardPolicies.CertificateConsumer,
                    Title = "Consume Certificates",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = [
                        StandardResourceActions.CertificateDownload,
                        StandardResourceActions.CertificateKeyDownload
                    ]
                },
                new() {
                    Id = StandardPolicies.CertificateAuthorityAdmin,
                    Title = "Certificate Authority Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = [
                        StandardResourceActions.CertificateAuthorityAdd,
                        StandardResourceActions.CertificateAuthorityUpdate,
                        StandardResourceActions.CertificateAuthorityDelete,
                        StandardResourceActions.CertificateAuthorityList
                    ]
                },
                new() {
                    Id = StandardPolicies.AcmeAccountAdmin,
                    Title = "ACME Account Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = [
                        StandardResourceActions.AcmeAccountList,
                        StandardResourceActions.AcmeAccountAdd,
                        StandardResourceActions.AcmeAccountUpdate,
                        StandardResourceActions.AcmeAccountDelete
                    ]
                },
                new() {
                    Id = StandardPolicies.StoredCredentialAdmin,
                    Title = "Stored Credential Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = [
                        StandardResourceActions.StoredCredentialList,
                        StandardResourceActions.StoredCredentialAdd,
                        StandardResourceActions.StoredCredentialUpdate,
                        StandardResourceActions.StoredCredentialDelete
                    ]
                },
                new() {
                    Id = StandardPolicies.StoredCredentialConsumer,
                    Title = "Stored Credential Consumer",
                    Description = "Provides access to fetch a decrypted stored credential.",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    IsResourceSpecific = true,
                    ResourceActions = [
                        StandardResourceActions.StoredCredentialReadSecret
                    ]
                },
                new() {
                    Id = StandardPolicies.ManagedChallengeAdmin,
                    Title = "Managed Challenge Administration",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    ResourceActions = [
                        StandardResourceActions.ManagedChallengeList,
                        StandardResourceActions.ManagedChallengeUpdate,
                        StandardResourceActions.ManagedChallengeDelete
                    ]
                },
                new() {
                    Id = StandardPolicies.ManagedChallengeConsumer,
                    Title = "Managed Challenge Consumer",
                    Description = "Allows consumer to request that a managed challenge be performed. When assigned with tag scopes, only challenges matching those tags are accessible.",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    IsResourceSpecific = true,
                    ResourceActions = [
                        StandardResourceActions.ManagedChallengeList,
                        StandardResourceActions.ManagedChallengeRequest,
                        StandardResourceActions.ManagedChallengeCleanup
                    ]
                },
                new() {
                    Id = StandardPolicies.ManagementHubAdmin,
                    Title = "Management Hub Admin",
                    Description = "Administer management hub.",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    IsResourceSpecific = true,
                    ResourceActions = [
                        StandardResourceActions.ManagementHubInstancesList,
                        StandardResourceActions.ManagementHubInstanceAdd,
                        StandardResourceActions.ManagementHubInstanceUpdate,
                        StandardResourceActions.ManagementHubInstanceDelete,
                        StandardResourceActions.SystemStatusList,
                        StandardResourceActions.SystemLogList,
                        StandardResourceActions.SystemCoreSettingsList,
                        StandardResourceActions.SystemCoreSettingsUpdate,
                        StandardResourceActions.SystemServiceConfigList,
                        StandardResourceActions.SystemServiceConfigUpdate,

                    ]
                },
                new() {
                    Id = StandardPolicies.ManagementHubReader,
                    Title = "Management Hub Reader",
                    Description = "View management hub.",
                    SecurityPermissionType = SecurityPermissionType.ALLOW,
                    IsResourceSpecific = true,
                    ResourceActions = [
                        StandardResourceActions.ManagementHubInstancesList,
                        StandardResourceActions.AcmeAccountList,
                        StandardResourceActions.CertificateAuthorityList,
                        StandardResourceActions.ChallengeProviderList,
                        StandardResourceActions.DeploymentTaskListProviders,
                        StandardResourceActions.ManagedChallengeList,
                        StandardResourceActions.ManagedItemList,
                        StandardResourceActions.ManagedItemLogView,
                        StandardResourceActions.StoredCredentialList,
                        StandardResourceActions.RoleList,
                        StandardResourceActions.TagList,
                        StandardResourceActions.TargetTypesList,
                        StandardResourceActions.SystemStatusList,
                        StandardResourceActions.SystemCoreSettingsList,
                        StandardResourceActions.SystemServiceConfigList,
                    ]
                },
                new() {
                     Id = StandardPolicies.ManagedInstance,
                     Title = "Management Hub Managed Instance",
                     Description = "Join management hub and allow to be managed by hub.",
                     SecurityPermissionType = SecurityPermissionType.ALLOW,
                     IsResourceSpecific = true,
                     ResourceActions = [
                         StandardResourceActions.ManagementHubInstanceJoin
                     ]
                 },
                  new() {
                 Id = StandardPolicies.ManagedInstanceSystemImport,
                 Title = "Instance Configuration Import",
                 Description = "Import system configuration and apply to a target instance",
                 SecurityPermissionType = SecurityPermissionType.ALLOW,
                 IsResourceSpecific = true,
                 ResourceActions = [
                     StandardResourceActions.ManagementHubInstanceImport
                 ]
             },
              new() {
                 Id = StandardPolicies.ManagedInstanceSystemExport,
                 Title = "Instance Configuration Export",
                 Description = "Export system configuration for a target instance",
                 SecurityPermissionType = SecurityPermissionType.ALLOW,
                 IsResourceSpecific = true,
                 ResourceActions = [
                     StandardResourceActions.ManagementHubInstanceExport
                 ]
              },
              new() {
                 Id = StandardPolicies.SystemUser,
                 Title = "System User",
                 Description = "Perform general system use actions",
                 SecurityPermissionType = SecurityPermissionType.ALLOW,
                 IsResourceSpecific = true,
                 ResourceActions = [
                     StandardResourceActions.SecurityPrincipalCheckAccess,
                     StandardResourceActions.SecurityPrincipalPasswordValidate,
                     StandardResourceActions.RoleList,
                 ]
              },
              new() {
                  Id = StandardPolicies.TagAdmin,
                  Title = "Tag Administration",
                  SecurityPermissionType = SecurityPermissionType.ALLOW,
                  ResourceActions = [
                      StandardResourceActions.TagList,
                      StandardResourceActions.TagAdd,
                      StandardResourceActions.TagUpdate,
                      StandardResourceActions.TagDelete
                  ]
              },
              new() {
                 Id = StandardPolicies.ManagedAcmeConsumer,
                 Title = "Managed Acme Consumer",
                 SecurityPermissionType = SecurityPermissionType.ALLOW,
                 ResourceActions = [
                     StandardResourceActions.ManagedAcmePerformOrder
                 ]
             },
                new() {
       Id = StandardPolicies.ManagedLicenseAdmin,
       Title = "Managed License Administration",
       SecurityPermissionType = SecurityPermissionType.ALLOW,
       ResourceActions = [
           StandardResourceActions.ManagedLicenseList,
           StandardResourceActions.ManagedLicenseAdd,
           StandardResourceActions.ManagedLicenseUpdate,
           StandardResourceActions.ManagedLicenseDelete,
           StandardResourceActions.ManagedLicenseActivate,
           StandardResourceActions.ManagedLicenseDeactivate,
           StandardResourceActions.ManagedLicenseStatus
       ]
   },
            ];
        }
    }

    /// <summary>
    /// Outcome of applying the standard access control config to the store, so a caller can report what an upgrade
    /// actually did. A run which wrote nothing and a run which failed to write anything are otherwise identical.
    /// </summary>
    public class StandardAccessConfigResult
    {
        public int ResourceActionsUpdated { get; set; }
        public int ResourcePoliciesUpdated { get; set; }
        public int RolesUpdated { get; set; }

        /// <summary>
        /// Items which could not be written or set up. Any entry here means the store does not match the standard
        /// config, so some roles grant less than they are declared to.
        /// </summary>
        public List<string> Failures { get; } = [];

        /// <summary>
        /// References which do not resolve, in the standard config itself or in the store after the update.
        /// These never throw: authorization resolves role -> policy -> action and a reference which is not there
        /// simply contributes nothing, so an incomplete role looks exactly like a correctly restrictive one.
        /// </summary>
        public List<string> IntegrityProblems { get; } = [];

        public bool IsSuccess => Failures.Count == 0 && IntegrityProblems.Count == 0;

        public override string ToString()
            => $"{ResourceActionsUpdated} resource action(s), {ResourcePoliciesUpdated} policy(s) and {RolesUpdated} role(s) updated"
                + $", {Failures.Count} failure(s), {IntegrityProblems.Count} integrity problem(s)";
    }

    public static class AccessControlConfig
    {
        /// <summary>
        /// Add/update standard system roles, policies and resource actions. Items which already match the standard config are left unchanged.
        /// </summary>
        /// <param name="access"></param>
        /// <returns></returns>
        public static async Task<StandardAccessConfigResult> UpdateStandardAccessConfig(IAccessControl access)
        {
            var result = new StandardAccessConfigResult();

            // check the config before applying it. A role which points at a policy that is not defined, or a policy
            // which grants an action that is not defined, is written to the store without error and then quietly
            // grants less than the role says it does, which is the failure this is here to catch.
            result.IntegrityProblems.AddRange(GetStandardConfigIntegrityProblems());

            // setup roles with policies

            var adminSvcPrincipal = AdminSecurityPrincipalId;

            // fetch the currently stored config so we only write items which are new or have changed, otherwise every startup rewrites (and audit logs) the entire standard config

            var storedActions = await access.GetResourceActions(adminSvcPrincipal) ?? [];
            var storedPolicies = await access.GetResourcePolicies(adminSvcPrincipal) ?? [];
            var storedRoles = await access.GetRoles(adminSvcPrincipal) ?? [];

            var actions = DistinctById(Policies.GetStandardResourceActions());

            foreach (var action in actions)
            {
                if (!IsResourceActionUnchanged(storedActions.FirstOrDefault(a => a.Id == action.Id), action))
                {
                    if (await access.AddResourceAction(adminSvcPrincipal, action, bypassIntegrityCheck: true))
                    {
                        result.ResourceActionsUpdated++;
                    }
                    else
                    {
                        result.Failures.Add($"Resource action [{action.Id}] could not be written to the store.");
                    }
                }
            }

            // setup policies with actions

            var policies = DistinctById(Policies.GetStandardPolicies());

            // add policies to store
            foreach (var r in policies)
            {
                if (!IsResourcePolicyUnchanged(storedPolicies.FirstOrDefault(p => p.Id == r.Id), r))
                {
                    if (await access.AddResourcePolicy(adminSvcPrincipal, r, bypassIntegrityCheck: true))
                    {
                        result.ResourcePoliciesUpdated++;
                    }
                    else
                    {
                        result.Failures.Add($"Resource policy [{r.Id}] could not be written to the store.");
                    }
                }
            }

            // setup roles with policies
            var roles = DistinctById(Policies.GetStandardRoles());

            foreach (var r in roles)
            {
                if (!IsRoleUnchanged(storedRoles.FirstOrDefault(role => role.Id == r.Id), r))
                {
                    // add roles and policy assignments to store
                    if (await access.AddRole(adminSvcPrincipal, r, bypassIntegrityCheck: true))
                    {
                        result.RolesUpdated++;
                    }
                    else
                    {
                        result.Failures.Add($"Role [{r.Id}] could not be written to the store.");
                    }
                }
            }

            // authorization reads the store, not the standard config, so confirm the update actually landed rather
            // than assuming it did. A partially applied update leaves assigned roles and access tokens working but
            // missing the actions the upgrade was supposed to grant them.
            result.IntegrityProblems.AddRange(await GetStoredConfigIntegrityProblems(access, adminSvcPrincipal));

            return result;
        }

        /// <summary>
        /// Problems in the standard config as declared in code: duplicate ids, or a role/policy referencing
        /// something which is not defined. Standard role, policy and resource action ids are permanent identifiers
        /// held by stored role assignments and scoped access tokens, so a reference which does not resolve is a
        /// config authoring mistake rather than a deliberate restriction.
        /// </summary>
        public static List<string> GetStandardConfigIntegrityProblems()
        {
            var problems = new List<string>();

            var actions = Policies.GetStandardResourceActions();
            var policies = Policies.GetStandardPolicies();
            var roles = Policies.GetStandardRoles();

            problems.AddRange(GetDuplicates(actions.Select(a => a.Id)).Select(id => $"Resource action [{id}] is declared more than once."));
            problems.AddRange(GetDuplicates(policies.Select(p => p.Id)).Select(id => $"Resource policy [{id}] is declared more than once."));
            problems.AddRange(GetDuplicates(roles.Select(r => r.Id)).Select(id => $"Role [{id}] is declared more than once."));

            var actionIds = new HashSet<string>(actions.Select(a => a.Id), StringComparer.Ordinal);
            var policyIds = new HashSet<string>(policies.Select(p => p.Id), StringComparer.Ordinal);

            foreach (var policy in policies)
            {
                var policyActions = policy.ResourceActions ?? [];

                problems.AddRange(policyActions
                    .Where(a => !actionIds.Contains(a))
                    .Select(a => $"Resource policy [{policy.Id}] grants resource action [{a}] which is not a standard resource action."));

                problems.AddRange(GetDuplicates(policyActions)
                    .Select(a => $"Resource policy [{policy.Id}] lists resource action [{a}] more than once."));
            }

            foreach (var role in roles)
            {
                var rolePolicies = role.Policies ?? [];

                problems.AddRange(rolePolicies
                    .Where(p => !policyIds.Contains(p))
                    .Select(p => $"Role [{role.Id}] references policy [{p}] which is not a standard resource policy."));

                problems.AddRange(GetDuplicates(rolePolicies)
                    .Select(p => $"Role [{role.Id}] lists policy [{p}] more than once."));
            }

            return problems;
        }

        /// <summary>
        /// Problems in the stored config after an update has been applied. Covers a standard item which failed to
        /// write, and a stored role or policy whose references no longer resolve, including roles which are no longer
        /// part of the standard config but are still assigned to security principals.
        /// </summary>
        private static async Task<List<string>> GetStoredConfigIntegrityProblems(IAccessControl access, string contextUserId)
        {
            var problems = new List<string>();

            var storedActions = await access.GetResourceActions(contextUserId) ?? [];
            var storedPolicies = await access.GetResourcePolicies(contextUserId) ?? [];
            var storedRoles = await access.GetRoles(contextUserId) ?? [];

            var storedActionIds = new HashSet<string>(storedActions.Select(a => a.Id), StringComparer.Ordinal);
            var storedPolicyIds = new HashSet<string>(storedPolicies.Select(p => p.Id), StringComparer.Ordinal);

            foreach (var standardRole in DistinctById(Policies.GetStandardRoles()))
            {
                var storedRole = storedRoles.FirstOrDefault(r => r.Id == standardRole.Id);

                if (storedRole == null)
                {
                    problems.Add($"Standard role [{standardRole.Id}] is not present in the store, so it grants nothing to the principals assigned to it.");
                }
                else if (!IsRoleUnchanged(storedRole, standardRole))
                {
                    problems.Add($"Stored role [{standardRole.Id}] does not match its standard definition after the update.");
                }
            }

            foreach (var role in storedRoles)
            {
                problems.AddRange((role.Policies ?? [])
                    .Where(p => !storedPolicyIds.Contains(p))
                    .Select(p => $"Stored role [{role.Id}] references policy [{p}] which is not in the store, so that policy grants nothing."));
            }

            foreach (var policy in storedPolicies)
            {
                problems.AddRange((policy.ResourceActions ?? [])
                    .Where(a => !storedActionIds.Contains(a))
                    .Select(a => $"Stored policy [{policy.Id}] grants resource action [{a}] which is not in the store."));
            }

            return problems;
        }

        /// <summary>
        /// The values which occur more than once in a sequence, each reported once.
        /// </summary>
        private static IEnumerable<string> GetDuplicates(IEnumerable<string> values)
        {
            return values.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
        }

        /// <summary>
        /// The standard config can currently declare more than one item with the same id (duplicate or aliased ids). When written to the store the
        /// last declaration wins, so use the same rule here to give a single definition per id and keep the config check idempotent.
        /// </summary>
        private static List<T> DistinctById<T>(List<T> items) where T : ConfigurationStoreItem
        {
            return items.GroupBy(i => i.Id).Select(g => g.Last()).ToList();
        }

        /// <summary>
        /// Compare the common stored item properties of an existing stored item against the standard config version of the same item.
        /// ItemType is not compared as it is a storage level discriminator rather than part of the standard config definition.
        /// </summary>
        private static bool IsStoredItemUnchanged(ConfigurationStoreItem? existing, ConfigurationStoreItem standard)
        {
            return existing != null
                && existing.Title == standard.Title
                && existing.Description == standard.Description;
        }

        private static bool IsResourceActionUnchanged(ResourceAction? existing, ResourceAction standard)
        {
            return IsStoredItemUnchanged(existing, standard)
                && existing!.ResourceType == standard.ResourceType;
        }

        private static bool IsResourcePolicyUnchanged(ResourcePolicy? existing, ResourcePolicy standard)
        {
            return IsStoredItemUnchanged(existing, standard)
                && existing!.SecurityPermissionType == standard.SecurityPermissionType
                && existing.IsResourceSpecific == standard.IsResourceSpecific
                && (existing.ResourceActions ?? []).SequenceEqual(standard.ResourceActions ?? []);
        }

        private static bool IsRoleUnchanged(Role? existing, Role standard)
        {
            return IsStoredItemUnchanged(existing, standard)
                && (existing!.Policies ?? []).SequenceEqual(standard.Policies ?? []);
        }

        /// <summary>
        /// Id of the built-in admin security principal. Standard config is written to the store as this principal.
        /// </summary>
        public const string AdminSecurityPrincipalId = "admin_01";

        /// <summary>
        /// Id of the service principal a hub uses to join itself as a managed instance.
        /// </summary>
        public const string ManagedInstanceSecurityPrincipalId = "managedinstance_sp_01";

        /// <summary>
        /// Title of the access token issued to the managed instance service principal.
        /// </summary>
        public const string ManagedInstanceJoiningTokenTitle = "Managed Instance Hub Joining Key";

        public static async Task<StandardAccessConfigResult> ConfigureStandardUsersAndRoles(IAccessControl access, ICredentialsManager creds)
        {
            // setup roles with policies
            var result = await UpdateStandardAccessConfig(access);

            // setup standard security principals

            // admin user
            var adminSpId = AdminSecurityPrincipalId;
            var managedInstanceSpId = ManagedInstanceSecurityPrincipalId;

            var users = await access.GetSecurityPrincipals(adminSpId) ?? [];

            // The default admin is only created when the store holds no security principals at all, i.e. a genuine
            // first run. Recreating it whenever this particular id is missing would resurrect, with the default
            // password, an account an administrator had deliberately removed, on the next service restart.
            if (users.Count == 0)
            {
                var adminSp = new SecurityPrincipal
                {
                    Id = adminSpId,
                    Description = "Primary default admin",
                    PrincipalType = SecurityPrincipalType.User,
                    Username = Environment.GetEnvironmentVariable("CERTIFY_ADMIN_DEFAULTUSERNAME") ?? "admin",
                    Password = Environment.GetEnvironmentVariable("CERTIFY_ADMIN_DEFAULTPWD") ?? "changeme!",
                    Provider = StandardIdentityProviders.INTERNAL,
                    IsBuiltIn = true
                };

                if (!await access.AddSecurityPrincipal(adminSp.Id, adminSp, bypassIntegrityCheck: true))
                {
                    result.Failures.Add($"The default admin security principal [{adminSpId}] could not be created.");
                    return result;
                }

                users = await access.GetSecurityPrincipals(adminSpId) ?? [];
            }

            if (!users.Any(u => u.Id == adminSpId))
            {
                // an established deployment which no longer holds the built-in admin. The remaining setup acts as
                // that principal, so it cannot run, but the standard roles and policies above are already applied.
                result.Failures.Add(
                    $"The built-in admin security principal [{adminSpId}] is not present, so standard service principal and access token setup was skipped.");

                return result;
            }

            // get assigned roles for admin and add the admin role if it is missing
            _ = await EnsureAssignedRole(access, result, adminSpId, adminSpId, StandardRoles.Administrator.Id);

            // add managed instance service principal if not already present
            if (!users.Any(u => u.Id == managedInstanceSpId))
            {
                var managedInstanceServicePrincipal = new SecurityPrincipal
                {
                    Id = managedInstanceSpId,
                    Title = "Managed Instances Service Principal",
                    PrincipalType = SecurityPrincipalType.Application,
                    Provider = StandardIdentityProviders.INTERNAL,
                    IsBuiltIn = true
                };

                if (!await access.AddSecurityPrincipal(adminSpId, managedInstanceServicePrincipal, bypassIntegrityCheck: true))
                {
                    result.Failures.Add($"The managed instance service principal [{managedInstanceSpId}] could not be created.");
                    return result;
                }
            }

            // The role assignment and joining token are checked on every startup rather than only when the principal
            // is first created, so an instance whose managed instance access was removed, or whose config was
            // restored without them, is repaired instead of failing later with no explanation.
            var managedInstanceAssignedRole = await EnsureAssignedRole(access, result, adminSpId, managedInstanceSpId, StandardRoles.ManagedInstance.Id);

            var (joiningToken, joiningTokenIsNew) = await EnsureManagedInstanceJoiningToken(access, result, adminSpId, managedInstanceSpId, managedInstanceAssignedRole);

            // if we don't have a stored credential as a client secret for the managed instance to join it's own hub, create one
            // direct instances don't really need this, but remote backends do so they can join back to their own hub.
            var existingJoiningKey = await creds.GetUnlockedCredential(HubSharedConstants.MgmtHubJoiningCredId);

            // a newly issued token also has to be written out, otherwise the stored credential keeps pointing at the
            // token it replaced and joining fails with credentials which look present but are not accepted
            if (joiningToken != null && (existingJoiningKey == null || joiningTokenIsNew))
            {
                var clientSecret = new ClientSecret { ClientId = joiningToken.ClientId, Secret = joiningToken.Secret };
                await creds.Update(new Config.StoredCredential
                {
                    StorageKey = HubSharedConstants.MgmtHubJoiningCredId,
                    ProviderType = StandardAuthTypes.STANDARD_AUTH_MGMTHUB,
                    Title = "Management Hub Joining Key",
                    Secret = System.Text.Json.JsonSerializer.Serialize(clientSecret)
                });
            }

            return result;
        }

        /// <summary>
        /// Ensure a security principal holds a role, returning the assignment.
        ///
        /// An existing assignment is returned unchanged: access tokens are scoped by AssignedRole id rather than by
        /// role id, so replacing an assignment here would silently strip the scope from every token pointing at it.
        /// </summary>
        private static async Task<AssignedRole?> EnsureAssignedRole(
            IAccessControl access,
            StandardAccessConfigResult result,
            string contextUserId,
            string securityPrincipalId,
            string roleId)
        {
            var assignedRoles = await access.GetAssignedRoles(contextUserId, securityPrincipalId) ?? [];

            var existing = assignedRoles.FirstOrDefault(a => a.RoleId == roleId);

            if (existing != null)
            {
                return existing;
            }

            var assignedRole = new AssignedRole
            {
                Id = Guid.NewGuid().ToString(),
                RoleId = roleId,
                SecurityPrincipalId = securityPrincipalId
            };

            if (!await access.AddAssignedRole(contextUserId, assignedRole, bypassIntegrityCheck: true))
            {
                result.Failures.Add($"Role [{roleId}] could not be assigned to security principal [{securityPrincipalId}].");
                return null;
            }

            return assignedRole;
        }

        /// <summary>
        /// Ensure the managed instance service principal holds a usable joining token, returning it and whether it
        /// had to be issued. An existing token is kept as it is, because managed instances already hold its secret.
        /// </summary>
        private static async Task<(AccessToken? Token, bool IsNew)> EnsureManagedInstanceJoiningToken(
            IAccessControl access,
            StandardAccessConfigResult result,
            string contextUserId,
            string securityPrincipalId,
            AssignedRole? scopedAssignedRole)
        {
            var assignedTokens = await access.GetAssignedAccessTokens(contextUserId) ?? [];

            var existingToken = assignedTokens
                .Where(t => t.SecurityPrincipalId == securityPrincipalId && t.Title == ManagedInstanceJoiningTokenTitle)
                .SelectMany(t => t.AccessTokens ?? [])
                .FirstOrDefault(a => a.DateRevoked == null && (a.DateExpiry == null || a.DateExpiry >= DateTimeOffset.UtcNow));

            if (existingToken != null)
            {
                return (existingToken, false);
            }

            if (scopedAssignedRole == null)
            {
                result.Failures.Add(
                    $"A managed instance joining token could not be issued because security principal [{securityPrincipalId}] does not hold the [{StandardRoles.ManagedInstance.Id}] role.");

                return (null, false);
            }

            var token = new AccessToken
            {
                ClientId = securityPrincipalId,
                Description = "System Generated",
                Secret = Guid.NewGuid().ToString().ToLowerInvariant(),
                TokenType = AccessTokenTypes.Simple,
                DateCreated = DateTime.UtcNow
            };

            var assignedApiAccessToken = new AssignedAccessToken
            {
                Id = Guid.NewGuid().ToString(),
                SecurityPrincipalId = securityPrincipalId,
                Title = ManagedInstanceJoiningTokenTitle,
                AccessTokens = [token],
                ScopedAssignedRoles = [
                    // scope assigned role is the id for AssignedRole (not the role id itself)
                    scopedAssignedRole.Id
                ],
            };

            if (!await access.AddAssignedAccessToken(contextUserId, assignedApiAccessToken))
            {
                result.Failures.Add("The managed instance joining token could not be written to the store.");
                return (null, false);
            }

            return (token, true);
        }
    }
}
