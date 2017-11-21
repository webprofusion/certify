namespace Certify.Models
{
    public class UpdateCheck
    {
        public AppVersion Version { get; set; }

        public UpdateMessage Message { get; set; }

        public bool IsNewerVersion { get; set; }

        public bool MustUpdate { get; set; }
    }
}