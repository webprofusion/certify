using Microsoft.Win32;
using System;
using System.Linq;

namespace Certify.Management
{
    public class SecurityProtocolsManager
    {
        #region Registry

        private RegistryKey GetRegistryBaseKey(RegistryHive hiveType)
        {
            if (Environment.Is64BitOperatingSystem)
            {
                return RegistryKey.OpenBaseKey(hiveType, RegistryView.Registry64);
            }
            else
            {
                return RegistryKey.OpenBaseKey(hiveType, RegistryView.Registry32);
            }
        }

        private void DisableSSLViaRegistry(string protocolKey)
        {
            //check if client key exists, if not create it
            //set \Client\DisabledByDefault=1

            //RegistryKey SSLProtocolsKey =  Registry.LocalMachine..OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\", true);
            RegistryKey SSLProtocolsKey = GetRegistryBaseKey(RegistryHive.LocalMachine).OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\", true);
            RegistryKey SSLProtocolKey = GetRegistryBaseKey(RegistryHive.LocalMachine).OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\" + protocolKey, true);

            //create key for protocol if it doesn't exist
            if (SSLProtocolKey == null)
            {
                SSLProtocolKey = SSLProtocolsKey.CreateSubKey(protocolKey);
            }

            //create Client key if required
            RegistryKey clientKey = SSLProtocolKey.OpenSubKey("Client", true);
            if (clientKey == null)
            {
                clientKey = SSLProtocolKey.CreateSubKey("Client");
            }

            //DisabledByDefault=1

            clientKey.SetValue("DisabledByDefault", 1, RegistryValueKind.DWord);
            clientKey.Close();
            //set \Server\Enabled=0
            RegistryKey serverKey = SSLProtocolKey.OpenSubKey("Server", true);
            if (serverKey == null)
            {
                serverKey = SSLProtocolKey.CreateSubKey("Server");
            }

            serverKey.SetValue("Enabled", 0, RegistryValueKind.DWord);
            serverKey.Close();
        }

        private void DisableSSLCipherViaRegistry(string cipher)
        {
            RegistryKey cipherTypesKey = GetRegistryBaseKey(RegistryHive.LocalMachine).OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Ciphers\", true);

            RegistryKey cipherKey = GetRegistryBaseKey(RegistryHive.LocalMachine).OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Ciphers\" + cipher, true);

            if (cipherKey == null)
            {
                cipherKey = cipherTypesKey.CreateSubKey(cipher);
            }

            cipherKey.SetValue("Enabled", 0, RegistryValueKind.DWord);
            cipherKey.Close();
        }

        #endregion Registry

        /// <summary>
        /// Add/update registry keys to disable insecure SSL/TLS protocols 
        /// </summary>
        public void PerformSSLProtocolLockdown()
        {
            DisableSSLViaRegistry("SSL 2.0");
            DisableSSLViaRegistry("SSL 3.0");

            DisableSSLCipherViaRegistry("DES 56/56");
            DisableSSLCipherViaRegistry("RC2 40/128");
            DisableSSLCipherViaRegistry("RC2 56/128");
            DisableSSLCipherViaRegistry("RC4 128/128");
            DisableSSLCipherViaRegistry("RC4 40/128");
            DisableSSLCipherViaRegistry("RC4 56/128");
            DisableSSLCipherViaRegistry("RC4 64/128");
            DisableSSLCipherViaRegistry("RC4 128/128");

            //TODO: enable other SSL
        }
    }
}