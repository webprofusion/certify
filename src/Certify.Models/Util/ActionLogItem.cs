using System;

namespace Certify.Models
{
    public class ActionLogItem
    {
        public string ManagedCertificateId { get; set; } = string.Empty;
        public DateTime DateTime { get; set; }
        public string Command { get; set; } = string.Empty;

        public string? Result
        {
            get; set;
        }

        public override string ToString() => "[" + DateTime.ToShortTimeString() + "] " + Command + (Result != null ? " : " + Result : "");
    }
}
