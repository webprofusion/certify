using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Shared;
using Certify.Providers.CertificateManagers;
using Certify.Providers.DeploymentTasks;
using Registration.Core.Models.Shared;

namespace Certify.Models.Plugins
{
    public interface ILicensingManager
    {
        Task<LicenseCheckResult> Validate(int productTypeId, string email, string key);

        Task<LicenseKeyInstallResult> RegisterInstall(int productTypeId, string email, string key, RegisteredInstance instance);

        bool FinaliseInstall(int productTypeId, LicenseKeyInstallResult result, string settingsPath);

        bool IsInstallRegistered(int productTypeId, string settingsPath);

        Task<bool> IsInstallActive(int productTypeId, string settingsPath);
    }

    public interface IDomainValidationType
    {
    }

    public interface IDashboardClient
    {
        Task<bool> SubmitFeedbackAsync(FeedbackReport feedback);

        Task<bool> ReportRenewalStatusAsync(RenewalStatusReport report);

        Task<bool> ReportServerStatusAsync();

        Task<bool> SignInAsync(string email, string pwd);

        Task<bool> RegisterInstance(RegisteredInstance instance, string email, string pwd, bool createAccount);

        Task<bool> ReportUserActionRequiredAsync(ItemActionRequired actionRequired);
    }

    public interface IProviderPlugin<TProviderInterface,TProviderDefinition>
    {
        List<TProviderDefinition> GetProviders(Type pluginType);
        TProviderInterface GetProvider(Type pluginType, string id);
    }

    /// <summary>
    /// Plugins which implement one or more deployment tasks implement this interface for dynamic plugin loading
    /// </summary>
    public interface IDeploymentTaskProviderPlugin: IProviderPlugin<IDeploymentTaskProvider, DeploymentProviderDefinition>
    {
    }

    /// <summary>
    /// Plugins which implement certificate managers implement this interface for dynamic login loading
    /// </summary>
    public interface ICertificateManagerProviderPlugin: IProviderPlugin<ICertificateManager, ProviderDefinition>
    {
    }
}
