using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Certify.Models
{
    public static class CertIdentifierType
    {
        public static string Dns { get; } = "dns";
        public static string Ip { get; } = "ip";
        public static string TnAuthList { get; } = "TNAuthList";
    }

    public class CertIdentifierItem
    {
        public string IdentifierType { get; set; } = CertIdentifierType.Dns;
        public string Value { get; set; } = string.Empty;
        public bool IsAuthorizationPending { get; set; }
        public string Status { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Value} [{IdentifierType}]";
        }

        public CertIdentifierItem() { }

        public CertIdentifierItem(string type, string domain)
        {
            IdentifierType = type;
            Value = domain;
        }

        public CertIdentifierItem(string domain)
        {
            Value = domain;
            IdentifierType = CertIdentifierType.Dns;
        }
    }
}
