using Certify.Models.Shared;
using Registration.Core.Models.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
}