using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Classes
{
    public class CertRequestConfig
    {
        public string Domain { get; set; }
        public string WebsiteRootPath { get; set; }
        public bool PerformChallengeFileCopy { get; set; }
    }
}
