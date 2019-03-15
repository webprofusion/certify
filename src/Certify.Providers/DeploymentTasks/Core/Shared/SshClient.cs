using System;
using System.Collections.Generic;
using System.IO;
using Certify.Config;
using Renci.SshNet;

namespace Certify.Providers.Deployment.Core.Shared
{
    public class SshConnectionConfig
    {
        public string Host { get; set; }
        public int Port { get; set; } = 22;

        public string Username { get; set; }
        public string Password { get; set; }
        public string PrivateKeyPath { get; set; }
        public string KeyPassphrase { get; set; }
    }

	public class CommandResult
    {
		public string Command { get; set; }
		public string Result { get; set; }
		public bool IsError { get; set; }
    }

    public class SshClient
    {
        SshConnectionConfig _config;

        public SshClient(SshConnectionConfig config)
        {
            _config = config;
        }

        public static SshConnectionConfig GetConnectionConfig(DeploymentTaskConfig config, Dictionary<string, string> credentials)
        {
            var sshConfig = new SshConnectionConfig
            {
                Host = config.TargetHost,
            };

            credentials.TryGetValue("username", out var username);
            if (username != null)
            {
                sshConfig.Username = username;
            }

            credentials.TryGetValue("password", out var password);
            if (password != null)
            {
                sshConfig.Password = password;
            }

            credentials.TryGetValue("privatekey", out var privatekey);
            if (privatekey != null)
            {
                sshConfig.PrivateKeyPath = privatekey;
            }

            credentials.TryGetValue("key_passphrase", out var passphrase);
            if (passphrase != null)
            {
                sshConfig.KeyPassphrase = passphrase;
            }
            return sshConfig;
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

        public List<CommandResult> ExecuteCommands(List<string> commands)
        {
         
            var results = new List<CommandResult>();

            var pk = GetPrivateKeyFile();

            using (var ssh = new Renci.SshNet.SshClient(_config.Host, _config.Username, pk))
            {
                try
                {
                    ssh.Connect();

                    foreach (var command in commands)
                    {
                        try
                        {
                            var cmd = ssh.RunCommand(command);

                            results.Add(new CommandResult { Command = command, Result = cmd.Result, IsError = false });
                        }
                        catch (Exception exp)
                        {
                            results.Add(new CommandResult { Command = command, Result = exp.ToString(), IsError = true });
                            break;
                        }
                    }

                    ssh.Disconnect();
                }
                catch (Exception e)
                {
                    Console.WriteLine("An exception has been caught " + e.ToString());
                }
            }

            return results;
        }

    }
}
