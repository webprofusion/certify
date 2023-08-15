using System;

namespace Certify.Models
{
    public class ActionLogItem
    {
        public string ManagedCertificateId { get; set; } = string.Empty;
        public DateTimeOffset EventDate { get; set; }
        public string Command { get; set; } = string.Empty;

        public string? Result
        {
            get; set;
        }

        public override string ToString() => "[" + EventDate.DateTime.ToShortTimeString() + "] " + Command + (Result != null ? " : " + Result : "");
    }
}
