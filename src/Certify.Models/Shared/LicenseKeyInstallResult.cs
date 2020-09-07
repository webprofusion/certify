namespace Certify.Models.Shared
{
    public class LicenseKeyInstallResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string UsageToken { get; set; }
    }
}
