using Certify.Models.Shared;
using Registration.Core.Models.Shared;
using System.Threading.Tasks;

namespace Certify.Models.Plugins
{
    public interface ILicensingManager
    {
        Task<LicenseCheckResult> Validate(int productTypeId, string email, string key);

        Task<LicenseKeyInstallResult> RegisterInstall(int productTypeId, string email, string key, string machineName);

        bool FinaliseInstall(int productTypeId, LicenseKeyInstallResult result, string settingsPath);

        bool IsInstallRegistered(int productTypeId, string settingsPath);
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

        Task<bool> RegisterInstance(RegisteredInstance instance);
    }
}