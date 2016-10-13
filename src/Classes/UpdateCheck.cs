using Certify.Classes;

namespace Certify.Classes
{
    public class UpdateCheck
    {
        public AppVersion Version { get; set; }

        public UpdateMessage Message { get; set; }

        public bool IsNewerVersion { get; set; }
    }
}