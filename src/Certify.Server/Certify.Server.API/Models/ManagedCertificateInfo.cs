using System;
using System.Collections.Generic;

namespace Certify.Server.Api.Public.Models
{
    public class ManagedCertificateInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public IEnumerable<string> Domains { get; set; }
        public string PrimaryDomain { get; set; }
        public DateTime? DateRenewed { get; set; }
        public DateTime? DateExpiry { get; set; }
        public string Status { get; set; }
    }
}
