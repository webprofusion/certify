using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class AppVersion
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }

        public static AppVersion FromString(string version)
        {
            string[] versionComponents = version.Split('.');

            AppVersion current = new AppVersion
            {
                Major = int.Parse(versionComponents[0]),
                Minor = int.Parse(versionComponents[1]),
                Patch = int.Parse(versionComponents[2])
            };
            return current;
        }

        public static bool IsOtherVersionNewer(AppVersion currentVersion, AppVersion otherVersion)
        {
            if (currentVersion.Major >= otherVersion.Major)
            {
                if (currentVersion.Major > otherVersion.Major)
                {
                    return false;
                }

                //current major version is same, check minor
                if (currentVersion.Minor >= otherVersion.Minor)
                {
                    if (currentVersion.Patch < otherVersion.Patch)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                //current minor version is less
                if (currentVersion.Minor < otherVersion.Minor)
                {
                    return true;
                }
            }
            else
            {
                //other Major version is newer
                return true;
            }

            return false; ;
        }
    }
}
