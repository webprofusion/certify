using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class CertificateRequestResult
    {
        public ManagedItem ManagedItem { get; set; }
        public bool IsSuccess { get; set; }
        public bool Abort { get; set; }
        public string Message { get; set; }
        public object Result { get; set; }
    }
}