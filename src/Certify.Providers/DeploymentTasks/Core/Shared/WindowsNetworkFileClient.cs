using System;
using System.Collections.Generic;
using System.IO;
using SimpleImpersonation;

namespace Certify.Providers.Deployment.Core.Shared
{
    public class WindowsNetworkFileClient
    {
        UserCredentials _credentials;

        public WindowsNetworkFileClient(UserCredentials credentials)
        {
            _credentials = credentials;
        }
        public List<string> ListFiles(string remoteDirectory)
        {

            var fileList = new List<string>();

            Impersonation.RunAsUser(_credentials, LogonType.Interactive, () =>
            {
                fileList.AddRange(Directory.GetFiles(remoteDirectory));

            });

            return fileList;

        }
        public bool CopyLocalToRemote(Dictionary<string, string> filesSrcDest)
        {
            // read source files as original user
            var destFiles = new Dictionary<string, byte[]>();
            foreach (var sourcePath in filesSrcDest.Keys)
            {
                var content = File.ReadAllBytes(sourcePath);
                destFiles.Add(filesSrcDest[sourcePath], content);
            }

            var isSuccess = true;
            // write new files as destination user
            Impersonation.RunAsUser(_credentials, LogonType.Interactive, () =>
            {
                foreach (var dest in destFiles)
                {
                    try
                    {
                        // For this test to pass the test user must have write permissions to the share and the underlying folder
                        File.WriteAllBytes(dest.Key, dest.Value);
                    }
                    catch (Exception)
                    {
                        // failed to copy the file. TODO: retries
                        isSuccess = false;
                        break;
                    }
                }

            });

            return isSuccess;
        }
    }
}
