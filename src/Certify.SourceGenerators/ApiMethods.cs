using System.Collections.Generic;
using SourceGenerator;

namespace Certify.SourceGenerators
{
    internal class ApiMethods
    {
        public static List<GeneratedAPI> GetApiDefinitions()
        {
            // declaring an API definition here is then used by the source generators to:
            // - create the public API endpoint
            // - map the call from the public API to the background service API in the service API Client (interface and implementation)
            // - to then generate the public API clients, run nswag when the public API is running.

            return new List<GeneratedAPI> {

                    new GeneratedAPI {

                        OperationName = "GetSecurityPrincipleAssignedRoles",
                        OperationMethod = "HttpGet",
                        Comment = "Get list of Assigned Roles for a given security principle",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "securityprinciple/{id}/assignedroles",
                        ServiceAPIRoute = "access/securityprinciple/{id}/assignedroles",
                        ReturnType = "ICollection<AssignedRole>",
                        Params =new Dictionary<string, string>{{"id","string"}}
                    },
                    new GeneratedAPI {

                        OperationName = "GetSecurityPrincipleRoleStatus",
                        OperationMethod = "HttpGet",
                        Comment = "Get list of Assigned Roles etc for a given security principle",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "securityprinciple/{id}/rolestatus",
                        ServiceAPIRoute = "access/securityprinciple/{id}/rolestatus",
                        ReturnType = "RoleStatus",
                        Params =new Dictionary<string, string>{{"id","string"}}
                    },
                    new GeneratedAPI {

                        OperationName = "GetAccessRoles",
                        OperationMethod = "HttpGet",
                        Comment = "Get list of available security Roles",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "roles",
                        ServiceAPIRoute = "access/roles",
                        ReturnType = "ICollection<Role>"
                    },
                    new GeneratedAPI {

                        OperationName = "GetSecurityPrinciples",
                        OperationMethod = "HttpGet",
                        Comment = "Get list of available security principles",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "securityprinciples",
                        ServiceAPIRoute = "access/securityprinciples",
                        ReturnType = "ICollection<SecurityPrinciple>"
                    },
                    new GeneratedAPI {
                        OperationName = "ValidateSecurityPrinciplePassword",
                        OperationMethod = "HttpPost",
                        Comment = "Check password valid for security principle",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "validate",
                        ServiceAPIRoute = "access/validate",
                        ReturnType = "Certify.Models.API.SecurityPrincipleCheckResponse",
                        Params = new Dictionary<string, string>{{"passwordCheck", "Certify.Models.API.SecurityPrinciplePasswordCheck" } }
                    },
                    new GeneratedAPI {

                        OperationName = "UpdateSecurityPrinciplePassword",
                        OperationMethod = "HttpPost",
                        Comment = "Update password for security principle",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "updatepassword",
                        ServiceAPIRoute = "access/updatepassword",
                        ReturnType = "Models.Config.ActionResult",
                        Params = new Dictionary<string, string>{{"passwordUpdate", "Certify.Models.API.SecurityPrinciplePasswordUpdate" } }
                    },
                    new GeneratedAPI {

                        OperationName = "AddSecurityPrinciple",
                        OperationMethod = "HttpPost",
                        Comment = "Add new security principle",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "securityprinciple",
                        ServiceAPIRoute = "access/securityprinciple",
                        ReturnType = "Models.Config.ActionResult",
                        Params = new Dictionary<string, string>{{"principle", "Certify.Models.Config.AccessControl.SecurityPrinciple" } }
                    },
                    new GeneratedAPI {

                        OperationName = "UpdateSecurityPrinciple",
                        OperationMethod = "HttpPost",
                        Comment = "Update existing security principle",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "securityprinciple/update",
                        ServiceAPIRoute = "access/securityprinciple/update",
                        ReturnType = "Models.Config.ActionResult",
                        Params = new Dictionary<string, string>{
                            { "principle", "Certify.Models.Config.AccessControl.SecurityPrinciple" }
                        }
                    },
                      new GeneratedAPI {

                        OperationName = "UpdateSecurityPrincipleAssignedRoles",
                        OperationMethod = "HttpPost",
                        Comment = "Update assigned roles for a security principle",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "securityprinciple/roles/update",
                        ServiceAPIRoute = "access/securityprinciple/roles/update",
                        ReturnType = "Models.Config.ActionResult",
                        Params = new Dictionary<string, string>{
                            { "update", "Certify.Models.Config.AccessControl.SecurityPrincipleAssignedRoleUpdate" }
                        }
                    },
                    new GeneratedAPI {

                        OperationName = "DeleteSecurityPrinciple",
                        OperationMethod = "HttpDelete",
                        Comment = "Delete security principle",
                        PublicAPIController = "Access",
                        PublicAPIRoute = "securityprinciple",
                        ServiceAPIRoute = "access/securityprinciple/{id}",
                        ReturnType = "Models.Config.ActionResult",
                        Params = new Dictionary<string, string>{{"id","string"}}
                    },
                    new GeneratedAPI {

                        OperationName = "GetAcmeAccounts",
                        OperationMethod = "HttpGet",
                        Comment = "Get All Acme Accounts",
                        PublicAPIController = "CertificateAuthority",
                        PublicAPIRoute = "accounts",
                        ServiceAPIRoute = "accounts",
                        ReturnType = "ICollection<Models.AccountDetails>"
                    },
                    new GeneratedAPI {

                        OperationName = "AddAcmeAccount",
                        OperationMethod = "HttpPost",
                        Comment = "Add New Acme Account",
                        PublicAPIController = "CertificateAuthority",
                        PublicAPIRoute = "account",
                        ServiceAPIRoute = "accounts",
                        ReturnType = "Models.Config.ActionResult",
                        Params =new Dictionary<string, string>{{"registration", "Certify.Models.ContactRegistration" } }
                    },
                    new GeneratedAPI {

                        OperationName = "AddCertificateAuthority",
                        OperationMethod = "HttpPost",
                        Comment = "Add New Certificate Authority",
                        PublicAPIController = "CertificateAuthority",
                        PublicAPIRoute = "authority",
                        ServiceAPIRoute = "accounts/authorities",
                        ReturnType = "Models.Config.ActionResult",
                        Params =new Dictionary<string, string>{{ "certificateAuthority", "Certify.Models.CertificateAuthority" } }
                    },
                     new GeneratedAPI {

                        OperationName = "RemoveManagedCertificate",
                        OperationMethod = "HttpDelete",
                        Comment = "Remove Managed Certificate",
                        PublicAPIController = "Certificate",
                        PublicAPIRoute = "certificate/{id}",
                        ServiceAPIRoute = "managedcertificates/delete/{id}",
                        ReturnType = "bool",
                        Params =new Dictionary<string, string>{{ "id", "string" } }
                    },
                    new GeneratedAPI {

                        OperationName = "RemoveCertificateAuthority",
                        OperationMethod = "HttpDelete",
                        Comment = "Remove Certificate Authority",
                        PublicAPIController = "CertificateAuthority",
                        PublicAPIRoute = "authority/{id}",
                        ServiceAPIRoute = "accounts/authorities/{id}",
                        ReturnType = "Models.Config.ActionResult",
                        Params =new Dictionary<string, string>{{ "id", "string" } }
                    },
                    new GeneratedAPI {
                        OperationName = "RemoveAcmeAccount",
                        OperationMethod = "HttpDelete",
                        Comment = "Remove ACME Account",
                        PublicAPIController = "CertificateAuthority",
                        PublicAPIRoute = "accounts/{storageKey}/{deactivate}",
                        ServiceAPIRoute = "accounts/remove/{storageKey}/{deactivate}",
                        ReturnType = "Models.Config.ActionResult",
                        Params =new Dictionary<string, string>{{ "storageKey", "string" }, { "deactivate", "bool" } }
                    },
                    new GeneratedAPI {
                        OperationName = "RemoveStoredCredential",
                        OperationMethod = "HttpDelete",
                        Comment = "Remove Stored Credential",
                        PublicAPIController = "StoredCredential",
                        PublicAPIRoute = "storedcredential/{storageKey}",
                        ServiceAPIRoute = "credentials",
                        ReturnType = "Models.Config.ActionResult",
                        Params =new Dictionary<string, string>{{ "storageKey", "string" } }
                    },
                    new GeneratedAPI {
                        OperationName = "PerformExport",
                        OperationMethod = "HttpPost",
                        Comment = "Perform an export of all settings",
                        PublicAPIController = "System",
                        PublicAPIRoute = "system/migration/export",
                        ServiceAPIRoute = "system/migration/export",
                        ReturnType = "Models.Config.Migration.ImportExportPackage",
                        Params =new Dictionary<string, string>{{ "exportRequest", "Certify.Models.Config.Migration.ExportRequest" } }
                    },
                     new GeneratedAPI {
                        OperationName = "PerformImport",
                        OperationMethod = "HttpPost",
                        Comment = "Perform an import of all settings",
                        PublicAPIController = "System",
                        PublicAPIRoute = "system/migration/import",
                        ServiceAPIRoute = "system/migration/import",
                        ReturnType = "ICollection<ActionStep>",
                        Params =new Dictionary<string, string>{{ "importRequest", "Certify.Models.Config.Migration.ImportRequest" } }
                    }
                };
        }
    }
}
