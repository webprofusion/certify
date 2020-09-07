namespace Certify.Models.API
{
    public static class Config
    {
        public static string APIBaseURI { get; } = "https://api.certifytheweb.com/v1/";
    }

    public class URLCheckResult
    {
        public bool IsAccessible { get; set; }
        public int? StatusCode { get; set; }
        public string Message { get; set; }
    }
}
