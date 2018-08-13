using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using Certify.Models;

namespace Certify.Utils
{
    public class Networking
    {
        public static List<BindingInfo> GetCertificateBindings()
        {
            try
            {
                var config = new SslCertBinding.Net.CertificateBindingConfiguration();
                var results = config.Query();

                return results.Select(b => new BindingInfo
                {
                    Host = b.ToString(),
                    IP = b.IpPort.Address.MapToIPv4().ToString(),
                    Port = b.IpPort.Port,
                    CertificateHash = b.Thumbprint
                }).ToList();
            }
            catch (System.IO.FileNotFoundException)
            {
                // failed to load the SslCertbinding dll
                return new List<BindingInfo>();
            }
            catch (Exception exp)
            {
                System.Diagnostics.Debug.WriteLine("Failed to query ssl bindings: " + exp.ToString());
                return new List<BindingInfo>();
            }
        }

        public static bool EndpointBindingExists(System.Net.IPEndPoint endpoint)
        {
            var config = new SslCertBinding.Net.CertificateBindingConfiguration();
            var results = config.Query(endpoint);
            if (results.Any())
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static List<IPAddressOption> GetIPAddresses()
        {
            var list = new List<IPAddressOption>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        var opt = new IPAddressOption
                        {
                            Description = $"{ip.Address.ToString()} [{ni.Name}]",
                            IPAddress = ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{ip.Address.ToString()}]" : ip.Address.ToString(),
                            IsIPv6 = (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        };
                        list.Add(opt);
                    }
                }
            }
            catch (Exception)
            {
                ; ; // could not retrieve networking information
            }
            return list;
        }
    }
}
