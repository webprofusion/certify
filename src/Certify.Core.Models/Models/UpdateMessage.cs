namespace Certify.Models
{
    public class UpdateMessage
    {
        public string Body { get; set; }
        public string DownloadPageURL { get; set; }
        public string ReleaseNotesURL { get; set; }
        public string DownloadFileURL { get; set; }

        /// <summary>
        /// If specified, all versions below the stated version require a mandatory update 
        /// </summary>
        public AppVersion MandatoryBelowVersion { get; set; }
    }
}