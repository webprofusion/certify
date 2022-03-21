namespace Certify.Models
{
    public class AppVersion
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }

        public override string ToString() => $"{Major}.{Minor}.{Patch}";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Not required")]
        public static AppVersion? FromString(string version)
        {
            try
            {
                var versionComponents = version.Split('.');

                var current = new AppVersion
                {
                    Major = int.Parse(versionComponents[0]),
                    Minor = int.Parse(versionComponents[1]),
                    Patch = int.Parse(versionComponents[2])
                };
                return current;
            }
            catch
            {
                return default;
            }
        }

        public static AppVersion FromVersion(System.Version version)
        {
            var current = new AppVersion
            {
                Major = version.Major,
                Minor = version.Minor,
                Patch = version.Build
            };
            return current;
        }

        public static bool IsOtherVersionNewer(AppVersion currentVersion, AppVersion otherVersion)
        {
            if (currentVersion == null)
            {
                return true;
            }

            if (otherVersion == null)
            {
                return false;
            }

            if (otherVersion.Major > currentVersion.Major)
            {
                return true;
            }

            if (otherVersion.Major == currentVersion.Major)
            {
                //current major version is same, check minor
                if (otherVersion.Minor > currentVersion.Minor)
                {
                    return true;
                }

                if (otherVersion.Minor == currentVersion.Minor)
                {
                    //majro and minor version the same, check patch level
                    if (otherVersion.Patch > currentVersion.Patch)
                    {
                        return true;
                    }
                }
            }

            // other version is not newer than current version
            return false;
        }
    }
}
