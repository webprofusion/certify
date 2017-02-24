using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class DomainOption
    {
        public string Domain { get; set; }
        public bool IsPrimaryDomain { get; set; }
        public bool IsSelected { get; set; }
    }
}