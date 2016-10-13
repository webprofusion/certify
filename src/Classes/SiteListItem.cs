using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Classes
{
     public class SiteListItem
    {
        public string Description
        {
            get
            {
                return SiteName + " - " + Protocol + "://" + Host + ":" + Port;
            }
        }
        public string SiteName { get; set; }
        public string Host { get; set; }
        public string PhysicalPath { get; set; }
        public bool IsHTTPS { get; set; }
        public string Protocol { get; set; }
        public int Port { get; set; }
        public bool HasCertificate { get; set; }
    }
}
