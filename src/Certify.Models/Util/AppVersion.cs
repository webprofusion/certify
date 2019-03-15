namespace Certify.Models
{
    public class AppVersion
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }

        public override string ToString() => $"{Major}.{Minor}.{Patch}";

        public static AppVersion FromString(string version)
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

        public static bool IsOtherVersionNewer(AppVersion currentVersion, AppVersion otherVersion)
        {
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
