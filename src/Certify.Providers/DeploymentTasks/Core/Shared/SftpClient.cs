using System;
using System.Collections.Generic;
using System.IO;
using Certify.Providers.DeploymentTasks;
using Renci.SshNet;

namespace Certify.Providers.Deployment.Core.Shared
{

    public class SftpClient
    {
        SshConnectionConfig _config;

        public SftpClient(SshConnectionConfig config)
        {
            _config = config;
        }

        private PrivateKeyFile GetPrivateKeyFile()
        {
            PrivateKeyFile pk = null;
            if (!string.IsNullOrEmpty(_config.KeyPassphrase))
            {
                pk = new PrivateKeyFile(_config.PrivateKeyPath, _config.KeyPassphrase);
            }
            else
            {
                pk = new PrivateKeyFile(_config.PrivateKeyPath);
            }
            return pk;
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

            // upload new files as destination user
            var pk = GetPrivateKeyFile();
            
            using (var sftp = new Renci.SshNet.SftpClient(_config.Host, _config.Username, pk))
            {
                try
                {
                    sftp.Connect();

                    foreach (var dest in destFiles)
                    {
                        try
                        {
                            using (var fileStream = new MemoryStream())
                            {
                                fileStream.Write(dest.Value, 0, dest.Value.Length);
                                fileStream.Position = 0;

                                sftp.UploadFile(fileStream, dest.Key);
                            }
                        }
                        catch (Exception exp)
                        {
                            System.Diagnostics.Debug.WriteLine(exp.ToString());
                            // failed to copy the file. TODO: retries
                            isSuccess = false;
                            break;
                        }
                    }
                    sftp.Disconnect();
                }
                catch (Exception e)
                {
                    Console.WriteLine("An exception has been caught " + e.ToString());
                }
            }

            return isSuccess;
        }

        /// <summary>
        /// List a remote directory in the console.
        /// </summary>
        public List<string> ListFiles(string remoteDirectory)
        {
            var pk = GetPrivateKeyFile();

            var fileList = new List<string>();

            using (var sftp = new Renci.SshNet.SftpClient(_config.Host, _config.Username, pk))
            {
                try
                {
                    sftp.Connect();

                    var files = sftp.ListDirectory(remoteDirectory);

                    foreach (var file in files)
                    {
                        fileList.Add(file.Name);
                    }

                    sftp.Disconnect();
                }
                catch (Renci.SshNet.Common.SshConnectionException e)
                {
                    throw new RemoteConnectionException(e.Message, e);
                }
                catch (Exception e)
                {
                    Console.WriteLine("An exception has been caught " + e.ToString());
                }
            }
            return fileList;
        }

    }
}
