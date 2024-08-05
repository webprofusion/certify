using System;
using System.Collections.Generic;
using System.IO;

namespace Certify.Models
{
    public class EnvironmentUtil
    {
        public static string CreateAppDataPath(string? subFolder = null)
        {
            var parts = new List<string>()
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Models.SharedConstants.APPDATASUBFOLDER
            };

            var path = Path.Combine(parts.ToArray());

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            if (subFolder != null)
            {
                parts.Add(subFolder);
                path = Path.Combine(parts.ToArray());

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }

            return path;
        }
    }
}
