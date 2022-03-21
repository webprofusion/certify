namespace Certify.Models.Shared
{
    public class LicenseKeyInstallResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string UsageToken { get; set; } = string.Empty;
    }
}
