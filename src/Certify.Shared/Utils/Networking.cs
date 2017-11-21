using Certify.Models;
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace Certify.Utils
{
    public class Networking
    {
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