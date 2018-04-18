namespace Certify.Models
{
    /// <summary>
    /// General info about a website local or remote website 
    /// </summary>
    public class SiteInfo
    {
        public StandardServerTypes ServerType { get; set; } = StandardServerTypes.IIS;
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}