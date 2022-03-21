namespace Certify.Models
{
    /// <summary>
    /// General info about a website local or remote website 
    /// </summary>
    public class SiteInfo
    {
        public StandardServerTypes ServerType { get; set; } = StandardServerTypes.IIS;
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool HasCertificate { get; set; }
    }
}
