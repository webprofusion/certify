using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models.Shared
{
    public class LicenseKeyInstallResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string UsageToken { get; set; }
    }
}