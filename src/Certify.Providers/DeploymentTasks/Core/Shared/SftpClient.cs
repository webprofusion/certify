using System;
using System.Collections.Generic;
using System.IO;
using Renci.SshNet;

namespace Certify.Providers.Deployment.Core.Shared
{
    public class SftpConnectionConfig
    {
        public string Host { get; set; }
        public string Username { get; set; }
        public string Passphrase { get; set; }
        public int Port { get; set; } = 22;
        public string PrivateKeyPath { get; set; }
    }

    public class SftpClient
    {
        SftpConnectionConfig _config;

        public SftpClient(SftpConnectionConfig config)
        {
            _config = config;
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
            var pk = new PrivateKeyFile(_config.PrivateKeyPath, _config.Passphrase);
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
            var pk = new PrivateKeyFile(_config.PrivateKeyPath, _config.Passphrase);

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
                catch (Exception e)
                {
                    Console.WriteLine("An exception has been caught " + e.ToString());
                }
            }
            return fileList;
        }
    }
}
