using System.Collections.Generic;
using System.Linq;
using System.Text;
using Certify.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerator
{
    public class GeneratedAPI
    {
        public string OperationName { get; set; } = string.Empty;
        public string OperationMethod { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string PublicAPIController { get; set; } = string.Empty;

        public string PublicAPIRoute { get; set; } = string.Empty;
        public List<PermissionSpec> RequiredPermissions { get; set; } = new List<PermissionSpec>();
        public bool UseManagementAPI { get; set; } = false;
        public string ManagementHubCommandType { get; set; } = string.Empty;
        public string ServiceAPIRoute { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public Dictionary<string, string> Params { get; set; } = new Dictionary<string, string>();
    }

    public class PermissionSpec
    {
        public string ResourceType { get; set; }
        public string Action { get; set; }
        public PermissionSpec(string resourceType, string action)
        {
            ResourceType = resourceType;
            Action = action;
        }
    }

    [Generator]
    public class PublicAPISourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var compilationProvider = context.CompilationProvider;
            var apiDefinitions = context.CompilationProvider
                .Select((compilation, _) => ApiMethods.GetApiDefinitions());

            var combined = compilationProvider.Combine(apiDefinitions);

            context.RegisterSourceOutput(combined, (spc, source) =>
            {
                var compilation = source.Left;
                var list = source.Right;
                var assemblyName = compilation.AssemblyName;

                foreach (var config in list)
                {
                    var paramSet = config.Params.ToList();
                    paramSet.Add(new KeyValuePair<string, string>("authContext", "AuthContext"));
                    var apiParamDecl = paramSet.Any() ? string.Join(", ", paramSet.Select(p => $"{p.Value} {p.Key}")) : "";
                    var apiParamDeclWithoutAuthContext = config.Params.Any() ? string.Join(", ", config.Params.Select(p => $"{p.Value} {p.Key}")) : "";

                    var apiParamCall = paramSet.Any() ? string.Join(", ", paramSet.Select(p => $"{p.Key}")) : "";
                    var apiParamCallWithoutAuthContext = config.Params.Any() ? string.Join(", ", config.Params.Select(p => $"{p.Key}")) : "";

                    if (assemblyName.EndsWith("Hub.Api") && !string.IsNullOrEmpty(config.PublicAPIController))
                    {
                        ImplementPublicAPI(spc, config, apiParamDeclWithoutAuthContext, apiParamDecl, apiParamCall);
                    }

                    if (assemblyName.EndsWith("Certify.UI.Blazor") && !string.IsNullOrEmpty(config.PublicAPIController))
                    {
                        ImplementAppModel(spc, config, apiParamDeclWithoutAuthContext, apiParamCallWithoutAuthContext);
                    }

                    if (assemblyName.EndsWith("Certify.Client") && !config.UseManagementAPI)
                    {
                        ImplementInternalAPIClient(spc, config, apiParamDecl, apiParamCall);
                    }
                }
            });
        }

        private static void ImplementAppModel(SourceProductionContext context, GeneratedAPI config, string apiParamDeclWithoutAuthContext, string apiParamCallWithoutAuthContext)
        {
            context.AddSource($"AppModel.{config.OperationName}.g.cs", SourceText.From($@"
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Certify.Models;
            using Certify.Models.Providers;
            using Certify.Models.Hub;
            using Certify.Models.Config;

            namespace Certify.UI.Client.Core
            {{
                public partial class AppModel
                {{
                    public async Task<{config.ReturnType}> {config.OperationName}({apiParamDeclWithoutAuthContext})
                    {{
                        return await _api.{config.OperationName}Async({apiParamCallWithoutAuthContext});
                    }}
                }}
            }}
            ", Encoding.UTF8));
        }

        private static void ImplementPublicAPI(SourceProductionContext context, GeneratedAPI config, string apiParamDeclWithoutAuthContext, string apiParamDecl, string apiParamCall)
        {
            var publicApiSrc = $@"

            using Certify.Client;
            using Certify.Models.Config;
            using Certify.Server.Hub.Api.Controllers;
            using Microsoft.AspNetCore.Authentication.JwtBearer;
            using Microsoft.AspNetCore.Authorization;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.Logging;
            using Certify.Models;
            using Certify.Models.Hub;

            namespace Certify.Server.Hub.Api.Controllers
            {{
                public partial class {config.PublicAPIController}Controller
                {{
                    /// <summary>
                    /// {config.Comment} [Generated]
                    /// </summary>
                    /// <returns></returns>
                    [{config.OperationMethod}]
                    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof({config.ReturnType}))]
                    [Route(""""""{config.PublicAPIRoute}"""""")]
                    public async Task<IActionResult> {config.OperationName}({apiParamDeclWithoutAuthContext})
                    {{

                        [RequiredPermissions]

                        var result = await {(config.UseManagementAPI ? "_mgmtAPI" : "_client")}.{config.OperationName}({apiParamCall.Replace("authContext", "CurrentAuthContext")});
                        return new OkObjectResult(result);
                    }}
                }}
            }}";

            if (config.RequiredPermissions.Any())
            {
                var fragment = "";
                foreach (var perm in config.RequiredPermissions)
                {
                    fragment += $@"

                            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!,  ""{perm.ResourceType}"" ,""{perm.Action}""));

                            if (!accessCheck.IsSuccess)
                            {{
                                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
                            }}
                      
                    ";
                }

                publicApiSrc = publicApiSrc.Replace("[RequiredPermissions]", fragment);
            }
            else
            {
                publicApiSrc = publicApiSrc.Replace("[RequiredPermissions]", "");
            }

            context.AddSource($"{config.PublicAPIController}Controller.{config.OperationName}.g.cs", SourceText.From(publicApiSrc, Encoding.UTF8));

            if (!string.IsNullOrEmpty(config.ManagementHubCommandType))
            {
                var src = $@"

                using Certify.Client;
                using Certify.Models.Hub;
                using Certify.Models;
                using Certify.Models.Config;
                using Certify.Models.Providers;
                using Certify.Models.Reporting;
                using Microsoft.AspNetCore.SignalR;

                namespace Certify.Server.Hub.Api.Services
                {{
                    public partial class ManagementAPI
                    {{
                        /// <summary>
                        /// {config.Comment} [Generated]
                        /// </summary>
                        /// <returns></returns>
                        internal async Task<{config.ReturnType}> {config.OperationName}({apiParamDecl})
                        {{
                            var args = new KeyValuePair<string, string>[] {{
                            {string.Join(",", config.Params.Select(s => $"new (\"{s.Key}\", {s.Key})").ToArray())}
                            }};

                            return await PerformInstanceCommandTaskWithResult<{config.ReturnType}>(instanceId, args, ""{config.ManagementHubCommandType}"") ?? [];
                        }}
                    }}
                }}";
                context.AddSource($"ManagementAPI.{config.OperationName}.g.cs", SourceText.From(src, Encoding.UTF8));
            }
        }

        private static void ImplementInternalAPIClient(SourceProductionContext context, GeneratedAPI config, string apiParamDecl, string apiParamCall)
        {
            var template = @"
            using Certify.Models;
            using Certify.Models.Config.Migration;
            using Certify.Models.Providers;
            using Certify.Models.Hub;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace Certify.Client
            {
               MethodTemplate
            }";

            if (config.OperationMethod == "HttpGet")
            {
                var code = template.Replace("MethodTemplate", $@"

                public partial interface ICertifyInternalApiClient
                {{
                    /// <summary>
                    /// {config.Comment} [Generated]
                    /// </summary>
                    /// <returns></returns>
                    Task<{config.ReturnType}> {config.OperationName}({apiParamDecl});
                }}

                public partial class CertifyApiClient
                {{
                    /// <summary>
                    /// {config.Comment} [Generated]
                    /// </summary>
                    /// <returns></returns>
                    public async Task<{config.ReturnType}> {config.OperationName}({apiParamDecl})
                    {{
                        var result = await FetchAsync($""{config.ServiceAPIRoute}"", authContext);
                        return JsonToObject<{config.ReturnType}>(result);
                    }}
                }}");
                var source = SourceText.From(code, Encoding.UTF8);
                context.AddSource($"{config.PublicAPIController}.{config.OperationName}.ICertifyInternalApiClient.g.cs", source);
            }

            if (config.OperationMethod == "HttpPost")
            {
                var postAPIRoute = config.ServiceAPIRoute;
                var postApiCall = apiParamCall;
                var postApiParamDecl = apiParamDecl;

                if (config.UseManagementAPI)
                {
                    postApiCall = apiParamCall.Replace("instanceId,", "");
                    postApiParamDecl = apiParamDecl.Replace("string instanceId,", "");
                }

                context.AddSource($"{config.PublicAPIController}.{config.OperationName}.ICertifyInternalApiClient.g.cs", SourceText.From(template.Replace("MethodTemplate", $@"

                public partial interface ICertifyInternalApiClient
                {{
                    /// <summary>
                    /// {config.Comment} [Generated]
                    /// </summary>
                    /// <returns></returns>
                    Task<{config.ReturnType}> {config.OperationName}({postApiParamDecl});
                }}

                public partial class CertifyApiClient
                {{
                    /// <summary>
                    /// {config.Comment} [Generated]
                    /// </summary>
                    /// <returns></returns>
                    public async Task<{config.ReturnType}> {config.OperationName}({postApiParamDecl})
                    {{
                        var result = await PostAsync($""{postAPIRoute}"", {postApiCall});
                        return JsonToObject<{config.ReturnType}>(await result.Content.ReadAsStringAsync());
                    }}
                }}"), Encoding.UTF8));
            }

            if (config.OperationMethod == "HttpDelete")
            {
                context.AddSource($"{config.PublicAPIController}.{config.OperationName}.ICertifyInternalApiClient.g.cs", SourceText.From(template.Replace("MethodTemplate", $@"

                public partial interface ICertifyInternalApiClient
                {{
                    /// <summary>
                    /// {config.Comment} [Generated]
                    /// </summary>
                    /// <returns></returns>
                    Task<{config.ReturnType}> {config.OperationName}({apiParamDecl});
                }}

                public partial class CertifyApiClient
                {{
                    /// <summary>
                    /// {config.Comment} [Generated]
                    /// </summary>
                    /// <returns></returns>
                    public async Task<{config.ReturnType}> {config.OperationName}({apiParamDecl})
                    {{
                        var route = $""{config.ServiceAPIRoute}""; 
                        var result = await DeleteAsync(route, authContext);
                        return JsonToObject<{config.ReturnType}>(await result.Content.ReadAsStringAsync());
                    }}
                }}"), Encoding.UTF8));
            }
        }
    }
}
