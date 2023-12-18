using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Certify.ACME.Anvil;
using Certify.Management;
using Certify.Models;
using Certify.Providers.ACME.Anvil;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Volumes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Serilog;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class CertifyManagerAccountTests
    {
        private static readonly bool _isContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
        private static readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly string _winRunnerTempDir = "C:\\Temp\\.step";
        private static string _caDomain;
        private static int _caPort;
        private static IContainer _caContainer;
        private static IVolume _stepVolume;
        private static Loggy _log;
        private CertifyManager _certifyManager;
        private CertificateAuthority _customCa;
        private AccountDetails _customCaAccount;

        [ClassInitialize]
        public static async Task ClassInit(TestContext context)
        {
            _log = new Loggy(new LoggerConfiguration().WriteTo.Debug().CreateLogger());

            _caDomain = _isContainer ? "step-ca" : "localhost";
            _caPort = 9000;

            await BootstrapStepCa();
            await CheckCustomCaIsRunning();
        }

        [TestInitialize]
        public async Task TestInit()
        {
            _certifyManager = new CertifyManager();
            _certifyManager.Init().Wait();

            await AddCustomCa();
            await AddNewCustomCaAccount();
            await CheckForExistingLeAccount();
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_customCaAccount != null)
            {
                await _certifyManager.RemoveAccount(_customCaAccount.StorageKey, true);
            }

            if (_customCa != null)
            {
                await _certifyManager.RemoveCertificateAuthority(_customCa.Id);
            }

            _certifyManager?.Dispose();
        }

        [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
        public static async Task ClassCleanup()
        {
            if (!_isContainer)
            {
                await _caContainer.DisposeAsync();
                if (_stepVolume != null)
                {
                    await _stepVolume.DeleteAsync();
                    await _stepVolume.DisposeAsync();
                }
                else
                {
                    Directory.Delete(_winRunnerTempDir, true);
                }
            }

            var stepConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".step", "config");
            if (Directory.Exists(stepConfigPath))
            {
                Directory.Delete(stepConfigPath, true);
            }

            var stepCertsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".step", "certs");
            if (Directory.Exists(stepCertsPath))
            {
                Directory.Delete(stepCertsPath, true);
            }
        }

        private static async Task BootstrapStepCa()
        {
            string stepCaFingerprint;

            // If running in a container
            if (_isContainer)
            {
                // Step container volume path containing step-ca config based on OS
                var configPath = _isWindows ? "C:\\step_share\\config\\defaults.json" : "/mnt/step_share/config/defaults.json";

                // Wait till step-ca config file is written
                while (!File.Exists(configPath)) { }

                // Read step-ca fingerprint from config file
                var stepCaConfigJson = JsonReader.ReadFile<StepCaConfig>(configPath);
                stepCaFingerprint = stepCaConfigJson.fingerprint;
            }
            else
            {
                var dockerInfo = RunCommand("docker", "info --format \"{{ .OSType }}\"", "Get Docker Info");
                var runningWindowsDockerEngine = dockerInfo.output.Contains("windows");

                // Start new step-ca container
                await StartStepCaContainer(runningWindowsDockerEngine);

                // Read step-ca fingerprint from config file
                if (_isWindows && runningWindowsDockerEngine)
                {
                    // Read step-ca fingerprint from config file
                    var stepCaConfigJson = JsonReader.ReadFile<StepCaConfig>($"{_winRunnerTempDir}\\config\\defaults.json");
                    stepCaFingerprint = stepCaConfigJson.fingerprint;
                } else
                {
                    var stepCaConfigBytes = await _caContainer.ReadFileAsync("/home/step/config/defaults.json");
                    var stepCaConfigJson = JsonReader.ReadBytes<StepCaConfig>(stepCaConfigBytes);
                    stepCaFingerprint = stepCaConfigJson.fingerprint;
                }
            }

            // Run bootstrap command
            var args = $"ca bootstrap -f --ca-url https://{_caDomain}:{_caPort} --fingerprint {stepCaFingerprint}";
            RunCommand("step", args, "Bootstrap Step CA Script", 1000 * 30);
        }

        private static async Task StartStepCaContainer(bool runningWindowsDockerEngine)
        {
            try
            {
                if (_isWindows && runningWindowsDockerEngine)
                {
                    if (!Directory.Exists(_winRunnerTempDir)) {
                        Directory.CreateDirectory(_winRunnerTempDir);
                    }

                    // Create new step-ca container
                    _caContainer = new ContainerBuilder()
                        .WithName("step-ca")
                        // Set the image for the container to "jrnelson90/step-ca-win:latest".
                        .WithImage("jrnelson90/step-ca-win:latest")
                        .WithBindMount(_winRunnerTempDir, "C:\\Users\\ContainerUser\\.step")
                        // Bind port 9000 of the container to port 9000 on the host.
                        .WithPortBinding(_caPort)
                        .WithEnvironment("DOCKER_STEPCA_INIT_NAME", "Smallstep")
                        .WithEnvironment("DOCKER_STEPCA_INIT_DNS_NAMES", _caDomain)
                        .WithEnvironment("DOCKER_STEPCA_INIT_REMOTE_MANAGEMENT", "true")
                        .WithEnvironment("DOCKER_STEPCA_INIT_ACME", "true")
                        // Wait until the HTTPS endpoint of the container is available.
                        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged($"Serving HTTPS on :{_caPort} ..."))
                        // Build the container configuration.
                        .Build();
                } 
                else
                {
                    // Create new volume for step-ca container
                    _stepVolume = new VolumeBuilder().WithName("step").Build();
                    await _stepVolume.CreateAsync();

                    // Create new step-ca container
                    _caContainer = new ContainerBuilder()
                        .WithName("step-ca")
                        // Set the image for the container to "smallstep/step-ca:latest".
                        .WithImage("smallstep/step-ca:latest")
                        .WithVolumeMount(_stepVolume, "/home/step")
                        // Bind port 9000 of the container to port 9000 on the host.
                        .WithPortBinding(_caPort)
                        .WithEnvironment("DOCKER_STEPCA_INIT_NAME", "Smallstep")
                        .WithEnvironment("DOCKER_STEPCA_INIT_DNS_NAMES", _caDomain)
                        .WithEnvironment("DOCKER_STEPCA_INIT_REMOTE_MANAGEMENT", "true")
                        .WithEnvironment("DOCKER_STEPCA_INIT_ACME", "true")
                        // Wait until the HTTPS endpoint of the container is available.
                        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged($"Serving HTTPS on :{_caPort} ..."))
                        // Build the container configuration.
                        .Build();
                }

                // Start step-ca container
                await _caContainer.StartAsync();
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static class JsonReader
        {
            public static T ReadFile<T>(string filePath)
            {
                using (var streamReader = new StreamReader(File.Open(filePath, FileMode.Open)))
                {
                    using (var jsonTextReader = new JsonTextReader(streamReader))
                    {
                        var serializer = new JsonSerializer();
                        return serializer.Deserialize<T>(jsonTextReader);
                    }
                }
            }

            public static T ReadBytes<T>(byte[] bytes)
            {
                using (var stringReader = new StringReader(Encoding.UTF8.GetString(bytes)))
                {
                    using (var jsonTextReader = new JsonTextReader(stringReader))
                    {
                        var serializer = new JsonSerializer();
                        return serializer.Deserialize<T>(jsonTextReader);
                    }
                }
            }
        }

        private class StepCaConfig
        {
            [JsonProperty(PropertyName = "ca-url")]
            public string ca_url;
            [JsonProperty(PropertyName = "ca-config")]
            public string ca_config;
            public string fingerprint;
            public string root;
        }

        private static CommandOutput RunCommand(string program, string args, string description = null, int timeoutMS = Timeout.Infinite)
        {
            if (description == null) { description = string.Concat(program, " ", args); }
            
            var output = "";
            var errorOutput = "";

            var startInfo = new ProcessStartInfo()
            {
                FileName = program,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process() { StartInfo = startInfo };

            process.OutputDataReceived += (obj, a) =>
            {
                if (!string.IsNullOrWhiteSpace(a.Data))
                {
                    _log.Information(a.Data);
                    output += a.Data;
                }
            };

            process.ErrorDataReceived += (obj, a) =>
            {
                if (!string.IsNullOrWhiteSpace(a.Data))
                {
                    _log.Error($"Error: {a.Data}");
                    errorOutput += a.Data;
                }
            };

            try
            {
                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit(timeoutMS); 
            }
            catch (Exception exp)
            {
                _log.Error($"Error Running ${description}: " + exp.ToString());
                throw;
            }

            _log.Information($"{description} is Finished");

            return new CommandOutput { errorOutput = errorOutput, output = output, exitCode = process.ExitCode };
        }

        private struct CommandOutput
        {
            public string errorOutput { get; set; }
            public string output { get; set; }
            public int exitCode { get; set; }
        }

        private static async Task CheckCustomCaIsRunning()
        {
            var httpHandler = new HttpClientHandler();

            httpHandler.ServerCertificateCustomValidationCallback = (message, certificate, chain, sslPolicyErrors) => true;

            var loggingHandler = new LoggingHandler(httpHandler, _log);
            var stepCaHttp = new HttpClient(loggingHandler);
            var healthRes = await stepCaHttp.GetAsync($"https://{_caDomain}:{_caPort}/health");
            var healthResStr = await healthRes.Content.ReadAsStringAsync();
            Assert.AreEqual("{\"status\":\"ok\"}\n", (healthResStr));
        }

        private async Task AddCustomCa()
        {
            _customCa = new CertificateAuthority
            {
                Id = "step-ca",
                Title = "Custom Step CA",
                IsCustom = true,
                IsEnabled = true,
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                ProductionAPIEndpoint = $"https://{_caDomain}:{_caPort}/acme/acme/directory",
                StagingAPIEndpoint = $"https://{_caDomain}:{_caPort}/acme/acme/directory",
                RequiresEmailAddress = true,
                AllowUntrustedTls = true,
                SANLimit = 100,
                StandardExpiryDays = 90,
                SupportedFeatures = new List<string>
                {
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_WILDCARD.ToString()
                },
                SupportedKeyTypes = new List<string>
                {
                    StandardKeyTypes.ECDSA256,
                }
            };
            var updateCaRes = await _certifyManager.UpdateCertificateAuthority(_customCa);
            Assert.IsTrue(updateCaRes.IsSuccess, $"Expected Custom CA creation for CA with ID {_customCa.Id} to be successful");
        }

        private async Task AddNewCustomCaAccount()
        {
            if (_customCa?.Id != null)
            {
                var contactRegistration = new ContactRegistration
                {
                    AgreedToTermsAndConditions = true,
                    CertificateAuthorityId = _customCa.Id,
                    EmailAddress = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com",
                    ImportedAccountKey = "",
                    ImportedAccountURI = "",
                    IsStaging = true
                };

                // Add account
                var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
                Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegistration.EmailAddress}");
                _customCaAccount = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegistration.EmailAddress);
            }
        }

        private async Task CheckForExistingLeAccount()
        {
            if ((await _certifyManager.GetAccountRegistrations()).Find(a => a.CertificateAuthorityId == "letsencrypt.org") == null)
            {
                var contactRegistration = new ContactRegistration
                {
                    AgreedToTermsAndConditions = true,
                    CertificateAuthorityId = "letsencrypt.org",
                    EmailAddress = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com",
                    ImportedAccountKey = "",
                    ImportedAccountURI = "",
                    IsStaging = true
                };

                // Add account
                var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
                Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegistration.EmailAddress}");
            }
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetAccountDetails()")]
        public async Task TestCertifyManagerGetAccountDetails()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when passed in managed certificate is null")]
        public async Task TestCertifyManagerGetAccountDetailsNullItem()
        {
            var caAccount = await _certifyManager.GetAccountDetails(null);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when allowCache is false")]
        public async Task TestCertifyManagerGetAccountDetailsAllowCacheFalse()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert, false);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when CertificateAuthorityId is defined in passed ManagedCertificate")]
        public async Task TestCertifyManagerGetAccountDetailsDefinedCertificateAuthorityId()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true, CertificateAuthorityId = _customCa.Id });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
            Assert.AreEqual(_customCa.Id, caAccount.CertificateAuthorityId, $"Unexpected certificate authority id '{caAccount.CertificateAuthorityId}'");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when OverrideAccountDetails is defined in CertifyManager")]
        public async Task TestCertifyManagerGetAccountDetailsDefinedOverrideAccountDetails()
        {
            var testUrl = "test.com";
            var account = new AccountDetails
            {
                AccountKey = "",
                AccountURI = "",
                Title = "Dev",
                Email = "test@certifytheweb.com",
                CertificateAuthorityId = _customCa.Id,
                StorageKey = "dev",
                IsStagingAccount = true,
            };
            _certifyManager.OverrideAccountDetails = account;

            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
            Assert.AreEqual("test@certifytheweb.com", caAccount.Email);

            _certifyManager.OverrideAccountDetails = null;
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when there is no matching account")]
        public async Task TestCertifyManagerGetAccountDetailsNoMatches()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true, CertificateAuthorityId = "sectigo-ev" });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert);
            Assert.IsNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when it is a resume order")]
        public async Task TestCertifyManagerGetAccountDetailsIsResumeOrder()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true, CertificateAuthorityId = "letsencrypt.org", LastAttemptedCA = "zerossl.com" });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert, true, false, true);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountDetails() when allowFailover is true")]
        public async Task TestCertifyManagerGetAccountDetailsAllowFailover()
        {
            var testUrl = "test.com";
            var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = testUrl, UseStagingMode = true });
            var caAccount = await _certifyManager.GetAccountDetails(dummyManagedCert, true, true);
            Assert.IsNotNull(caAccount, "Expected result of CertifyManager.GetAccountDetails() to not be null");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.AddAccount()")]
        public async Task TestCertifyManagerAddAccount()
        {
            AccountDetails accountDetails = null;
            try
            {
                // Setup account registration info
                var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
                var contactRegistration = new ContactRegistration
                {
                    AgreedToTermsAndConditions = true,
                    CertificateAuthorityId = _customCa.Id,
                    EmailAddress = contactRegEmail,
                    ImportedAccountKey = "",
                    ImportedAccountURI = "",
                    IsStaging = true
                };

                // Add account
                var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
                Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
                accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
                Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            }
            finally
            {
                // Cleanup added account
                if (accountDetails != null)
                {
                    await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
                }
            }
        }

        [TestMethod, Description("Happy path test for using CertifyManager.RemoveAccount()")]
        public async Task TestCertifyManagerRemoveAccount()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Remove account
            var removeAccountRes = await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
            Assert.IsTrue(removeAccountRes.IsSuccess, $"Expected account removal to be successful for {contactRegEmail}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when AgreedToTermsAndConditions is false")]
        public async Task TestCertifyManagerAddAccountDidNotAgree()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = false,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "You must agree to the terms and conditions of the Certificate Authority to register with them.", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when CertificateAuthorityId is a bad value")]
        public async Task TestCertifyManagerAddAccountBadCaId()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "bad_ca.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "Invalid Certificate Authority specified.", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when ImportedAccountKey is a blank value")]
        public async Task TestCertifyManagerAddAccountMissingAccountKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = _customCaAccount.AccountURI,
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "To import account details both the existing account URI and account key in PEM format are required. ", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when ImportedAccountURI is a blank value")]
        public async Task TestCertifyManagerAddAccountMissingAccountUri()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = _customCaAccount.AccountKey,
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "To import account details both the existing account URI and account key in PEM format are required. ", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when ImportedAccountKey is a bad value")]
        public async Task TestCertifyManagerAddAccountBadAccountKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "tHiSiSnOtApEm",
                ImportedAccountURI = _customCaAccount.AccountURI,
                IsStaging = true
            };

            // Attempt to add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsFalse(addAccountRes.IsSuccess, $"Expected account creation to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(addAccountRes.Message, "The provided account key was invalid or not supported for import. A PEM (text) format RSA or ECDA private key is required.", "Unexpected error message");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNull(accountDetails, $"Did not expect an account for {contactRegEmail} to be returned by CertifyManager.GetAccountRegistrations()");
        }

        [TestMethod, Description("Test for CertifyManager.AddAccount() when ImportedAccountKey and ImportedAccountURI are valid")]
        public async Task TestCertifyManagerAddAccountImport()
        {
            // Remove account
            var removeAccountRes = await _certifyManager.RemoveAccount(_customCaAccount.StorageKey);
            Assert.IsTrue(removeAccountRes.IsSuccess, $"Expected account removal to be successful for {_customCaAccount.Email}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == _customCaAccount.Email);
            Assert.IsNull(accountDetails, $"Did not expect an account for {_customCaAccount.Email} to be returned by CertifyManager.GetAccountRegistrations()");

            // Setup account registration info
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = _customCaAccount.Email,
                ImportedAccountKey = _customCaAccount.AccountKey,
                ImportedAccountURI = _customCaAccount.AccountURI,
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {_customCaAccount.Email}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == _customCaAccount.Email);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {_customCaAccount.Email}");
        }

        [TestMethod, Description("Test for using CertifyManager.RemoveAccount() with a bad storage key")]
        public async Task TestCertifyManagerRemoveAccountBadKey()
        {
            // Attempt to remove account with bad storage key
            var badStorageKey = "8da1a662-18ed-4787-a0b1-dc36db5a866b";
            var removeAccountRes = await _certifyManager.RemoveAccount(badStorageKey, true);
            Assert.IsFalse(removeAccountRes.IsSuccess, $"Expected account removal to be unsuccessful for storage key {badStorageKey}");
            Assert.AreEqual(removeAccountRes.Message, "Account not found.", "Unexpected error message");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.GetAccountAndACMEProvider()")]
        public async Task TestCertifyManagerGetAccountAndAcmeProvider()
        {
            AccountDetails accountDetails = null;
            try
            {
                // Setup account registration info
                var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
                var contactRegistration = new ContactRegistration
                {
                    AgreedToTermsAndConditions = true,
                    CertificateAuthorityId = _customCa.Id,
                    EmailAddress = contactRegEmail,
                    ImportedAccountKey = "",
                    ImportedAccountURI = "",
                    IsStaging = true
                };

                // Add account
                var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
                Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
                accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
                Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

                var (account, certAuthority, acmeProvider) = await _certifyManager.GetAccountAndACMEProvider(accountDetails.StorageKey);
                Assert.IsNotNull(account, $"Expected account returned by GetAccountAndACMEProvider() to not be null for storage key {accountDetails.StorageKey}");
                Assert.IsNotNull(certAuthority, $"Expected certAuthority returned by GetAccountAndACMEProvider() to not be null for storage key {accountDetails.StorageKey}");
                Assert.IsNotNull(acmeProvider, $"Expected acmeProvider returned by GetAccountAndACMEProvider() to not be null for storage key {accountDetails.StorageKey}");
            }
            finally
            {
                // Cleanup added account
                if (accountDetails != null)
                {
                    await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
                }
            }
        }

        [TestMethod, Description("Test for using CertifyManager.GetAccountAndACMEProvider() with a bad storage key")]
        public async Task TestCertifyManagerGetAccountAndAcmeProviderBadKey()
        {
            // Attempt to retrieve account with bad storage key
            var badStorageKey = "8da1a662-18ed-4787-a0b1-dc36db5a866b";
            var (account, certAuthority, acmeProvider) = await _certifyManager.GetAccountAndACMEProvider(badStorageKey);
            Assert.IsNull(account, $"Expected account returned by GetAccountAndACMEProvider() to be null for storage key {badStorageKey}");
            Assert.IsNull(certAuthority, $"Expected certAuthority returned by GetAccountAndACMEProvider() to be null for storage key {badStorageKey}");
            Assert.IsNull(acmeProvider, $"Expected acmeProvider returned by GetAccountAndACMEProvider() to be null for storage key {badStorageKey}");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.UpdateAccountContact()")]
        public async Task TestCertifyManagerUpdateAccountContact()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Update account
            var newContactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var newContactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = newContactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };
            var updateAccountRes = await _certifyManager.UpdateAccountContact(accountDetails.StorageKey, newContactRegistration);
            Assert.IsTrue(updateAccountRes.IsSuccess, $"Expected account creation to be successful for {newContactRegEmail}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == newContactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {newContactRegEmail}");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Test for using CertifyManager.UpdateAccountContact() when AgreedToTermsAndConditions is false")]
        public async Task TestCertifyManagerUpdateAccountContactNoAgreement()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Update account
            var newContactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var newContactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = false,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = newContactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };
            var updateAccountRes = await _certifyManager.UpdateAccountContact(accountDetails.StorageKey, newContactRegistration);
            Assert.IsFalse(updateAccountRes.IsSuccess, $"Expected account creation to not be successful for {newContactRegEmail}");
            Assert.AreEqual(updateAccountRes.Message, "You must agree to the terms and conditions of the Certificate Authority to register with them.", "Unexpected error message");
            var newAccountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == newContactRegEmail);
            Assert.IsNull(newAccountDetails, $"Expected none of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {newContactRegEmail}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Test for using CertifyManager.UpdateAccountContact() when passed storage key doesn't exist")]
        public async Task TestCertifyManagerUpdateAccountContactBadKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Update account
            var newContactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var newContactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = newContactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };
            var badStorageKey = Guid.NewGuid().ToString();
            var updateAccountRes = await _certifyManager.UpdateAccountContact(badStorageKey, newContactRegistration);
            Assert.IsFalse(updateAccountRes.IsSuccess, $"Expected account creation to not be successful for {newContactRegEmail}");
            Assert.AreEqual(updateAccountRes.Message, "Account not found.", "Unexpected error message");
            var newAccountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == newContactRegEmail);
            Assert.IsNull(newAccountDetails, $"Expected none of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {newContactRegEmail}");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Happy path test for using CertifyManager.ChangeAccountKey()")]
        public async Task TestCertifyManagerChangeAccountKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            var firstAccountKey = accountDetails.AccountKey;

            // Update account key
            var newKeyPem = KeyFactory.NewKey(KeyAlgorithm.ES256).ToPem();
            var changeAccountKeyRes = await _certifyManager.ChangeAccountKey(accountDetails.StorageKey, newKeyPem);
            Assert.IsTrue(changeAccountKeyRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            Assert.AreEqual(changeAccountKeyRes.Message, "Completed account key rollover", "Unexpected message for CertifyManager.GetAccountRegistrations() success");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            Assert.AreNotEqual(firstAccountKey, accountDetails.AccountKey, $"Expected account key for {contactRegEmail} to have changed after successful CertifyManager.ChangeAccountKey()");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Happy path test for using CertifyManager.ChangeAccountKey() with no passed in new account key")]
        public async Task TestCertifyManagerChangeAccountKeyNull()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = "letsencrypt.org",
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            var firstAccountKey = accountDetails.AccountKey;

            // Update account key
            var changeAccountKeyRes = await _certifyManager.ChangeAccountKey(accountDetails.StorageKey);
            Assert.IsTrue(changeAccountKeyRes.IsSuccess, $"Expected account creation to be successful for {contactRegEmail}");
            Assert.AreEqual(changeAccountKeyRes.Message, "Completed account key rollover", "Unexpected message for CertifyManager.GetAccountRegistrations() success");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            Assert.AreNotEqual(firstAccountKey, accountDetails.AccountKey, $"Expected account key for {contactRegEmail} to have changed after successful CertifyManager.ChangeAccountKey()");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Test for using CertifyManager.ChangeAccountKey() when passed an invalid storage key")]
        public async Task TestCertifyManagerChangeAccountKeyBadStorageKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account key update to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            var firstAccountKey = accountDetails.AccountKey;

            // Attempt to update account key
            var newKeyPem = KeyFactory.NewKey(KeyAlgorithm.ES256).ToPem();
            var badStorageKey = Guid.NewGuid().ToString();
            var changeAccountKeyRes = await _certifyManager.ChangeAccountKey(badStorageKey, newKeyPem);
            Assert.IsFalse(changeAccountKeyRes.IsSuccess, $"Expected account key update to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(changeAccountKeyRes.Message, "Failed to match account to known ACME provider", "Unexpected error message for CertifyManager.GetAccountRegistrations() failure");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            Assert.AreEqual(firstAccountKey, accountDetails.AccountKey, $"Expected account key for {contactRegEmail} not to have changed after unsuccessful CertifyManager.ChangeAccountKey()");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Test for using CertifyManager.ChangeAccountKey() when passed an invalid new account key")]
        public async Task TestCertifyManagerChangeAccountKeyBadAccountKey()
        {
            // Setup account registration info
            var contactRegEmail = "admin." + Guid.NewGuid().ToString().Substring(0, 6) + "@test.com";
            var contactRegistration = new ContactRegistration
            {
                AgreedToTermsAndConditions = true,
                CertificateAuthorityId = _customCa.Id,
                EmailAddress = contactRegEmail,
                ImportedAccountKey = "",
                ImportedAccountURI = "",
                IsStaging = true
            };

            // Add account
            var addAccountRes = await _certifyManager.AddAccount(contactRegistration);
            Assert.IsTrue(addAccountRes.IsSuccess, $"Expected account key update to be successful for {contactRegEmail}");
            var accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            var firstAccountKey = accountDetails.AccountKey;

            // Attempt to update account key
            var badKeyPem = KeyFactory.NewKey(KeyAlgorithm.ES256).ToPem().Substring(20);
            var changeAccountKeyRes = await _certifyManager.ChangeAccountKey(accountDetails.StorageKey, badKeyPem);
            Assert.IsFalse(changeAccountKeyRes.IsSuccess, $"Expected account key update to be unsuccessful for {contactRegEmail}");
            Assert.AreEqual(changeAccountKeyRes.Message, "Failed to use provide key for account rollover", "Unexpected error message for CertifyManager.GetAccountRegistrations() failure");
            accountDetails = (await _certifyManager.GetAccountRegistrations()).Find(a => a.Email == contactRegEmail);
            Assert.IsNotNull(accountDetails, $"Expected one of the accounts returned by CertifyManager.GetAccountRegistrations() to be for {contactRegEmail}");
            Assert.AreEqual(firstAccountKey, accountDetails.AccountKey, $"Expected account key for {contactRegEmail} not to have changed after unsuccessful CertifyManager.ChangeAccountKey()");

            // Cleanup account
            await _certifyManager.RemoveAccount(accountDetails.StorageKey, true);
        }

        [TestMethod, Description("Happy path test for using CertifyManager.UpdateCertificateAuthority() to add a new custom CA")]
        public async Task TestCertifyManagerUpdateCertificateAuthorityAdd()
        {
            CertificateAuthority newCustomCa = null;
            try
            {
                newCustomCa = new CertificateAuthority
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Test Custom CA",
                    IsCustom = true,
                    IsEnabled = true,
                    SupportedFeatures = new List<string>
                    {
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    }
                };
                var updateCaRes = await _certifyManager.UpdateCertificateAuthority(newCustomCa);
                Assert.IsTrue(updateCaRes.IsSuccess, $"Expected Custom CA creation for CA with ID {newCustomCa.Id} to be successful");
                Assert.AreEqual(updateCaRes.Message, "OK", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() success");
                var certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
                var newCaDetails = certificateAuthorities.Find(c => c.Id == newCustomCa.Id);
                Assert.IsNotNull(newCaDetails, $"Expected one of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {newCustomCa.Id}");
            }
            finally
            {
                if (newCustomCa != null)
                {
                    await _certifyManager.RemoveCertificateAuthority(newCustomCa.Id);
                }
            }
        }

        [TestMethod, Description("Happy path test for using CertifyManager.UpdateCertificateAuthority() to update an existing custom CA")]
        public async Task TestCertifyManagerUpdateCertificateAuthorityUpdate()
        {
            CertificateAuthority newCustomCa = null;
            try
            {
                newCustomCa = new CertificateAuthority
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "Test Custom CA",
                    IsCustom = true,
                    IsEnabled = true,
                    AllowInternalHostnames = false,
                    SupportedFeatures = new List<string>
                    {
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    }
                };

                // Add new CA
                var addCaRes = await _certifyManager.UpdateCertificateAuthority(newCustomCa);
                Assert.IsTrue(addCaRes.IsSuccess, $"Expected Custom CA creation for CA with ID {newCustomCa.Id} to be successful");
                Assert.AreEqual(addCaRes.Message, "OK", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() success");
                var certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
                var newCaDetails = certificateAuthorities.Find(c => c.Id == newCustomCa.Id);
                Assert.IsNotNull(newCaDetails, $"Expected one of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {newCustomCa.Id}");
                Assert.IsFalse(newCaDetails.AllowInternalHostnames);

                var updatedCustomCa = new CertificateAuthority
                {
                    Id = newCustomCa.Id,
                    Title = "Test Custom CA",
                    IsCustom = true,
                    IsEnabled = true,
                    AllowInternalHostnames = true,
                    SupportedFeatures = new List<string>
                    {
                        CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    }
                };

                // Update existing CA
                var updateCaRes = await _certifyManager.UpdateCertificateAuthority(updatedCustomCa);
                Assert.IsTrue(updateCaRes.IsSuccess, $"Expected Custom CA update for CA with ID {updatedCustomCa.Id} to be successful");
                Assert.AreEqual(updateCaRes.Message, "OK", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() success");
                certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
                newCaDetails = certificateAuthorities.Find(c => c.Id == updatedCustomCa.Id);
                Assert.IsNotNull(newCaDetails, $"Expected one of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {updatedCustomCa.Id}");
                Assert.IsTrue(newCaDetails.AllowInternalHostnames);
            }
            finally
            {
                if (newCustomCa != null)
                {
                    await _certifyManager.RemoveCertificateAuthority(newCustomCa.Id);
                }
            }
        }

        [TestMethod, Description("Test for using CertifyManager.UpdateCertificateAuthority() on a default CA")]
        public async Task TestCertifyManagerUpdateCertificateAuthorityDefaultCa()
        {
            var certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
            var defaultCa = certificateAuthorities.First();
            var newCustomCa = new CertificateAuthority
            {
                Id = defaultCa.Id,
                Title = "Test Custom CA",
                IsCustom = true,
                IsEnabled = true,
                AllowInternalHostnames = false,
                SupportedFeatures = new List<string>
                {
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                }
            };

            // Attempt to update default CA
            var updateCaRes = await _certifyManager.UpdateCertificateAuthority(newCustomCa);
            Assert.IsFalse(updateCaRes.IsSuccess, $"Expected CA update for default CA with ID {defaultCa.Id} to be unsuccessful");
            Assert.AreEqual(updateCaRes.Message, "Default Certificate Authorities cannot be modified.", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() failure");
        }

        [TestMethod, Description("Happy path test for using CertifyManager.RemoveCertificateAuthority()")]
        public async Task TestCertifyManagerRemoveCertificateAuthority()
        {
            var newCustomCa = new CertificateAuthority
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Test Custom CA",
                IsCustom = true,
                IsEnabled = true,
                SupportedFeatures = new List<string>
                {
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                }
            };

            // Add custom CA
            var updateCaRes = await _certifyManager.UpdateCertificateAuthority(newCustomCa);
            Assert.IsTrue(updateCaRes.IsSuccess, $"Expected Custom CA creation for CA with ID {newCustomCa.Id} to be successful");
            Assert.AreEqual(updateCaRes.Message, "OK", "Unexpected result message for CertifyManager.UpdateCertificateAuthority() success");
            var certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
            var newCaDetails = certificateAuthorities.Find(c => c.Id == newCustomCa.Id);
            Assert.IsNotNull(newCaDetails, $"Expected one of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {newCustomCa.Id}");

            // Delete custom CA
            var deleteCaRes = await _certifyManager.RemoveCertificateAuthority(newCustomCa.Id);
            Assert.IsTrue(deleteCaRes.IsSuccess, $"Expected Custom CA deletion for CA with ID {newCustomCa.Id} to be successful");
            Assert.AreEqual(deleteCaRes.Message, "OK", "Unexpected result message for CertifyManager.RemoveCertificateAuthority() success");
            certificateAuthorities = await _certifyManager.GetCertificateAuthorities();
            newCaDetails = certificateAuthorities.Find(c => c.Id == newCustomCa.Id);
            Assert.IsNull(newCaDetails, $"Expected none of the CAs returned by CertifyManager.GetCertificateAuthorities() to have an ID of {newCustomCa.Id}");
        }

        [TestMethod, Description("Test for using CertifyManager.RemoveCertificateAuthority() when passed a bad custom CA ID")]
        public async Task TestCertifyManagerRemoveCertificateAuthorityBadId()
        {
            var badId = Guid.NewGuid().ToString();

            // Delete custom CA
            var deleteCaRes = await _certifyManager.RemoveCertificateAuthority(badId);
            Assert.IsFalse(deleteCaRes.IsSuccess, $"Expected Custom CA deletion for CA with ID {badId} to be unsuccessful");
            Assert.AreEqual(deleteCaRes.Message, $"The certificate authority {badId} was not found in the list of custom CAs and could not be removed.", "Unexpected result message for CertifyManager.RemoveCertificateAuthority() failure");
        }
    }
}
