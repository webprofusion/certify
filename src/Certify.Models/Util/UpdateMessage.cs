using System.Collections.Generic;

namespace Certify.Models
{
    public class ReleaseNotes
    {
        public string Version { get; set; } = string.Empty;
        public string ReleaseDate { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }

    public class UpdateMessage
    {
        public string Body { get; set; } = string.Empty;
        public string DownloadPageURL { get; set; } = string.Empty;
        public string ReleaseNotesURL { get; set; } = string.Empty;
        public string DownloadFileURL { get; set; } = string.Empty;
        public string SHA256 { get; set; } = string.Empty;

        /// <summary>
        /// If specified, all versions below the stated version require a mandatory update 
        /// </summary>
        public AppVersion? MandatoryBelowVersion { get; set; }

        public List<ReleaseNotes> ReleaseNotes { get; set; } = new();
    }
}
