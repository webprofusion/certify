using System.Collections.Generic;

namespace Certify.Models
{
    public class ReleaseNotes
    {
        public string Version { get; set; }
        public string ReleaseDate { get; set; }
        public string Body { get; set; }
    }

    public class UpdateMessage
    {
        public string Body { get; set; }
        public string DownloadPageURL { get; set; }
        public string ReleaseNotesURL { get; set; }
        public string DownloadFileURL { get; set; }
        public string SHA256 { get; set; }

        /// <summary>
        /// If specified, all versions below the stated version require a mandatory update 
        /// </summary>
        public AppVersion MandatoryBelowVersion { get; set; }

        public List<ReleaseNotes> ReleaseNotes { get; set; }
    }
}
