using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Challenges.DNS;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges
{
    public class ChallengeProviders
    {

        public class CredentialsRequiredException : Exception
        {
        }

        public class BuiltinDnsProviderProvider : IDnsProviderProviderPlugin
        {
            private static List<ChallengeProviderDefinition> _providers;

            static BuiltinDnsProviderProvider()
            {
                _providers = new List<ChallengeProviderDefinition>()
                {
                    // IIS
                    new ChallengeProviderDefinition
                    {
                        Id = "HTTP01.IIS.Local",
                        ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
                        Title = "Local IIS Server",
                        Description = "Validates via standard http website bindings on port 80",
                        HandlerType = ChallengeHandlerType.INTERNAL
                    },

                    // Fake challenge type for UN/PW authentication
                    new ChallengeProviderDefinition
                    {
                        Id = StandardAuthTypes.STANDARD_AUTH_GENERIC,
                        ChallengeType = "",
                        Title = "Username and Password",
                        Description = "Standard username and password credentials",
                        HandlerType = ChallengeHandlerType.INTERNAL,
                        ProviderParameters= new List<ProviderParameter>
                        {
                           new ProviderParameter{ Key="username",Name="Username", IsRequired=true, IsPassword=false, IsCredential=true },
                           new ProviderParameter{ Key="password",Name="Password", IsRequired=true, IsPassword=true, IsCredential=true },
                        }
                    },

                    // Fake challenge type for password-only authentication
                    new ChallengeProviderDefinition
                    {
                        Id = StandardAuthTypes.STANDARD_AUTH_PASSWORD,
                        ChallengeType = "",
                        Title = "Password",
                        Description = "Standard Password credential",
                        HandlerType = ChallengeHandlerType.INTERNAL,
                        ProviderParameters= new List<ProviderParameter>
                        {
                           new ProviderParameter{ Key="password",Name="Password", IsRequired=true, IsPassword=true, IsCredential=true },
                        }
                    },

                      // Fake challenge type for api token authentication
                    new ChallengeProviderDefinition
                    {
                        Id = StandardAuthTypes.STANDARD_AUTH_API_TOKEN,
                        ChallengeType = "",
                        Title = "API Key or Token",
                        Description = "Generic API Token or Key",
                        HandlerType = ChallengeHandlerType.INTERNAL,
                        ProviderParameters= new List<ProviderParameter>
                        {
                           new ProviderParameter{ Key="api_token",Name="API Key or Token", IsRequired=true, IsPassword=true, IsCredential=true },
                        }
                    },

                    // Fake challenge type for Windows network credentials
                    new ChallengeProviderDefinition
                    {
                        Id = StandardAuthTypes.STANDARD_AUTH_WINDOWS,
                        ChallengeType = "",
                        Title = "Windows Credentials (Network)",
                        Description = "Windows username and password credentials",
                        HandlerType = ChallengeHandlerType.INTERNAL,
                        ProviderParameters= new List<ProviderParameter>
                        {
                           new ProviderParameter{ Key="domain",Name="Domain", IsRequired=false, IsPassword=false, IsCredential=true },
                           new ProviderParameter{ Key="username",Name="Username", IsRequired=true, IsPassword=false, IsCredential=true },
                           new ProviderParameter{ Key="password",Name="Password", IsRequired=true, IsPassword=true, IsCredential=true },
                        }
                    },

                    // Fake challenge type for Windows impersonation credentials
                    new ChallengeProviderDefinition
                    {
                        Id = StandardAuthTypes.STANDARD_AUTH_LOCAL_AS_USER,
                        ChallengeType = "",
                        Title = "Windows Credentials (Local)",
                        Description = "Windows username and password credentials",
                        HandlerType = ChallengeHandlerType.INTERNAL,
                        ProviderParameters= new List<ProviderParameter>
                        {
                           new ProviderParameter{ Key="domain",Name="Domain", IsRequired=false, IsPassword=false, IsCredential=true, Description="(optional)" },
                           new ProviderParameter{ Key="username",Name="Username", IsRequired=true, IsPassword=false, IsCredential=true },
                           new ProviderParameter{ Key="password",Name="Password", IsRequired=true, IsPassword=true, IsCredential=true },
                        }
                    },

                    // Fake challenge type for SSH UN/PW or UN/key credentials
                    new ChallengeProviderDefinition
                    {
                        Id = StandardAuthTypes.STANDARD_AUTH_SSH,
                        ChallengeType = "",
                        Title = "SSH Credentials",
                        Description = "SSH username, password and private key credentials",
                        HandlerType = ChallengeHandlerType.INTERNAL,
                        ProviderParameters= new List<ProviderParameter>
                        {
                           new ProviderParameter{ Key="username",Name="Username", IsRequired=true, IsPassword=false, IsCredential=true },
                           new ProviderParameter{ Key="password",Name="Password", IsRequired=false, IsPassword=true, IsCredential=true, Description="Optional password" },
                           new ProviderParameter{ Key="privatekey",Name="Private Key File Path", IsRequired=false, IsPassword=false, IsCredential=true, Description="Optional path private key file" },
                           new ProviderParameter{ Key="key_passphrase",Name="Private Key Passphrase", IsRequired=false, IsPassword=true, IsCredential=true , Description="Optional key passphrase"},
                        }
                    },

                    // Fake challenge type for Azure AD OAuth client credentials
                    new ChallengeProviderDefinition
                    {
                        Id = "ExternalAuth.Azure.ClientSecret",
                        Title = "Azure AD Application Client Secret",
                        Description = "Azure AD Application user and client secret",
                        HelpUrl="https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal",
                        ProviderParameters = new List<ProviderParameter>{
                            new ProviderParameter{Key="tenantid", Name="Directory (tenant) Id", IsRequired=true, IsCredential=true },
                            new ProviderParameter{Key="clientid", Name="Application (client) Id", IsRequired=true, IsCredential=true },
                            new ProviderParameter{Key="secret",Name="Client Secret", IsRequired=true , IsPassword=true}
                        },
                        ChallengeType = "",
                        HandlerType = ChallengeHandlerType.INTERNAL
                    },

                    // DNS by pausing and e-mailing a manual request
                    DnsProviderManual.Definition,

                    // DNS by using a PowerShell script
                    DnsProviderScripting.Definition,

                    // ISSUE: Apache Libcloud's Python provider has no definitions.
                };
            }

            public IDnsProvider GetProvider(Type pluginType, string id)
            {
                if (id == DnsProviderManual.Definition.Id)
                {
                    return new DnsProviderManual();
                }
                else if (id == DnsProviderScripting.Definition.Id)
                {
                    return new DnsProviderScripting();
                }
                else
                {
                    return null;
                }
            }

            public List<ChallengeProviderDefinition> GetProviders(Type pluginType)
            {
                return _providers.ToList(); // Return a copy so it can't be inadvertently mutated
            }
        }

        public static async Task<IDnsProvider> GetDnsProvider(string providerType, Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            IDnsProvider dnsAPIProvider = null;

            if (!string.IsNullOrEmpty(providerType))
            {
                var providerPlugins = PluginManager.CurrentInstance.DnsProviderProviders;
                foreach (var providerPlugin in providerPlugins)
                {
                    dnsAPIProvider = providerPlugin.GetProvider(providerPlugin.GetType(), providerType);
                    if (dnsAPIProvider != null)
                    {
                        break;
                    }
                }
            }
            else
            {
                return null;
            }

            if (dnsAPIProvider == null)
            {
                // we don't have the requested provider available, plugin probably didn't load or ID is wrong
                if (providerType == "DNS01.API.MSDNS")
                {
                    // We saved earlier that the MSDNS provider failed to load. It's now explicitly being requested, so log that failure.
                    log?.Error("Failed to create MS DNS API Provider. Check Microsoft.Management.Infrastructure is available and install latest compatible Windows Management Framework: https://docs.microsoft.com/en-us/powershell/wmf/overview");
                    return null;
                }
                else
                {
                    log?.Error($"Cannot create requested DNS API Provider. Plugin did not load or provider ID is invalid: {providerType}");
                    return null;
                }
            }
            else
            {
                await dnsAPIProvider.InitProvider(credentials, parameters, log);
            }

            return dnsAPIProvider;
        }

        public static async Task<List<ChallengeProviderDefinition>> GetChallengeAPIProviders()
        {
            var result = PluginManager.CurrentInstance.DnsProviderProviders.SelectMany(pp => pp.GetProviders(pp.GetType())).ToList();
            return await Task.FromResult(result);
        }
    }
}
