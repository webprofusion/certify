using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models.API
{
    public class URLCheckResult
    {
        public bool IsAccessible { get; set; }
        public int? StatusCode { get; set; }
        public string Message { get; set; }
    }
}