namespace Certify.Models.Shared
{
    public static class DocumentationLinks
    {
        private static string DOCS_BASE = "https://docs.certifytheweb.com/docs";

        public static string GETTING_STARTED = DOCS_BASE + "/intro";
        public static string ABOUT_LETSENCRYPT = DOCS_BASE + "/letsencrypt";

        public static string ABOUT_VALIDATION_HTTP = DOCS_BASE + "/http-validation";
        public static string ABOUT_VALIDATION_DNS = DOCS_BASE + "/dns-validation";
    }
}
