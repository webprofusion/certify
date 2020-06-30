using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Certify.Core.Management.Challenges.DNS;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.DNS.AcmeDns;
using Certify.Providers.DNS.Aliyun;
using Certify.Providers.DNS.AWSRoute53;
using Certify.Providers.DNS.Azure;
using Certify.Providers.DNS.Cloudflare;
using Certify.Providers.DNS.DnsMadeEasy;
using Certify.Providers.DNS.GoDaddy;
using Certify.Providers.DNS.MSDNS;
using Certify.Providers.DNS.NameCheap;
using Certify.Providers.DNS.OVH;
using Certify.Providers.DNS.SimpleDNSPlus;
using Certify.Providers.DNS.TransIP;

namespace Certify.Core.Management.Challenges
{
    public class ChallengeProviders
    {

        public class CredentialsRequiredException : Exception
        {
        }

        public static async Task<IDnsProvider> GetDnsProvider(string providerType, Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            ChallengeProviderDefinition providerDefinition;
            IDnsProvider dnsAPIProvider = null;

            if (!string.IsNullOrEmpty(providerType))
            {
                providerDefinition = (await ChallengeProviders.GetChallengeAPIProviders()).FirstOrDefault(p => p.Id == providerType);
            }
            else
            {
                return null;
            }

            if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.PYTHON_HELPER)
            {
                if (credentials == null || !credentials.Any())
                {
                    throw new CredentialsRequiredException();
                }

                dnsAPIProvider = new LibcloudDNSProvider(credentials);
            }
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.INTERNAL)
            {
                if (credentials == null)
                {
                    throw new CredentialsRequiredException();
                }

                // instantiate/initialise the required DNS provider
                if (providerDefinition.Id == DnsProviderAWSRoute53.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderAWSRoute53(credentials);
                }
                else if (providerDefinition.Id == DnsProviderAzure.Definition.Id)
                {
                    var azureDns = new DnsProviderAzure(credentials);

                    dnsAPIProvider = azureDns;
                }
                else if (providerDefinition.Id == DnsProviderCloudflare.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderCloudflare(credentials);
                }
                else if (providerDefinition.Id == DnsProviderGoDaddy.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderGoDaddy(credentials);
                }
                else if (providerDefinition.Id == DnsProviderSimpleDNSPlus.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderSimpleDNSPlus(credentials);
                }
                else if (providerDefinition.Id == DnsProviderDnsMadeEasy.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderDnsMadeEasy(credentials);
                }
                else if (providerDefinition.Id == DnsProviderOvh.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderOvh(credentials);
                }
                else if (providerDefinition.Id == DnsProviderAliyun.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderAliyun(credentials);
                }
                else if (providerDefinition.Id == "DNS01.API.MSDNS") // DnsProviderMSDNS.Definition.Id - avoid instantiating provider due to possible dll loading issues
                {
                    dnsAPIProvider = TryGetMsDNSProvider(credentials, parameters, log);
                }
                else if (providerDefinition.Id == DnsProviderAcmeDns.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderAcmeDns(credentials, parameters, Util.GetAppDataFolder());
                }
                else if (providerDefinition.Id == DnsProviderNameCheap.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderNameCheap(credentials);
                }
                else if (providerDefinition.Id == DnsProviderTransIP.Definition.Id)
                {
                    dnsAPIProvider = new DnsProviderTransIP(credentials);
                }
            }
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.MANUAL)
            {
                if (providerDefinition.Id == DNS.DnsProviderManual.Definition.Id)
                {
                    dnsAPIProvider = new DNS.DnsProviderManual();
                }
            }
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.CUSTOM_SCRIPT)
            {
                if (providerDefinition.Id == DNS.DnsProviderScripting.Definition.Id)
                {
                    dnsAPIProvider = new DNS.DnsProviderScripting(parameters);
                }
            }
            else if (providerDefinition.HandlerType == Models.Config.ChallengeHandlerType.POWERSHELL)
            {
                if (providerDefinition.Config.Contains("Provider=Certify.Providers.DNS.PoshACME"))
                {
                    var scriptPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Scripts\DNS\PoshACME");
                    
                    // TODO : move this out, shared config should be injected
                    var config = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

                    var ps = new DNS.DnsProviderPoshACME(parameters, credentials, scriptPath, config.PowershellExecutionPolicy);

                    ps.DelegateProviderDefinition = providerDefinition;

                    dnsAPIProvider = ps;

                }
            }

            if (dnsAPIProvider != null)
            {
                await dnsAPIProvider.InitProvider(parameters, log);
            }

            return dnsAPIProvider;
        }

        public static IDnsProvider TryGetMsDNSProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log)
        {
            try
            {
                return new DnsProviderMSDNS(credentials, parameters);
            }
            catch
            {
                log?.Error("Failed to create MS DNS API Provider. Check Microsoft.Management.Infrastructure is available and install latest compatible Windows Management Framework: https://docs.microsoft.com/en-us/powershell/wmf/overview");
                return null;
            };
        }

        public static async Task<List<ChallengeProviderDefinition>> GetChallengeAPIProviders()
        {
            var providers = new List<ChallengeProviderDefinition>
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
                new ChallengeProviderDefinition
                {
                    Id = "ExternalAuth.Azure.ClientSecret",
                    Title = "Azure AD Application Client Secret",
                    Description = "Azure AD Application user and client secret",

                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{Key="tenantid", Name="Directory (tenant) Id", IsRequired=true, IsCredential=true },
                        new ProviderParameter{Key="clientid", Name="Application (client) Id", IsRequired=true, IsCredential=true },
                        new ProviderParameter{Key="secret",Name="Client Secret", IsRequired=true , IsPassword=true}
                    },
                    ChallengeType = "",
                    HandlerType = ChallengeHandlerType.INTERNAL
                },

        // DNS
        DnsProviderManual.Definition,
                DnsProviderScripting.Definition,
                Providers.DNS.AWSRoute53.DnsProviderAWSRoute53.Definition,
                Providers.DNS.Azure.DnsProviderAzure.Definition,
                Providers.DNS.Cloudflare.DnsProviderCloudflare.Definition,
                Providers.DNS.GoDaddy.DnsProviderGoDaddy.Definition,
                Providers.DNS.SimpleDNSPlus.DnsProviderSimpleDNSPlus.Definition,
                Providers.DNS.DnsMadeEasy.DnsProviderDnsMadeEasy.Definition,
                Providers.DNS.OVH.DnsProviderOvh.Definition,
                Providers.DNS.Aliyun.DnsProviderAliyun.Definition,
                Providers.DNS.AcmeDns.DnsProviderAcmeDns.Definition,
                Providers.DNS.NameCheap.DnsProviderNameCheap.Definition,
                Providers.DNS.TransIP.DnsProviderTransIP.Definition
            };

            // TODO : load config from file


            try
            {
                TryAddProviders(providers);
            }
            catch { }

            return await Task.FromResult(providers);
        }

        private static void TryAddProviders(List<ChallengeProviderDefinition> providers)
        {
            // some providers may fail to add due to platform dependencies/restrictions
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    providers.AddRange(Certify.Core.Management.Challenges.DNS.DnsProviderPoshACME.ExtendedProviders);

                    providers.Add(Providers.DNS.MSDNS.DnsProviderMSDNS.Definition);
                }
            }
            catch { }

        }
    }
}
