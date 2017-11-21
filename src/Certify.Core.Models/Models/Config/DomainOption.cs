using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class DomainOption: BindableBase
    {
        /// <summary>
        /// Domain name we are managing
        /// </summary>
        public string Domain { get; set; }

        /// <summary>
        /// If true, this item is the primary subject for the certificate request
        /// </summary>
        public bool IsPrimaryDomain { get; set; }

        /// <summary>
        /// If false, we are currently skipping this item for the certificate request
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// If true, domain is not present in site bindings but is implicit by IP/dns wilcard etc and
        /// is being specified manually
        /// </summary>
        public bool IsManualEntry { get; set; }

        public string Title { get; set; }
    }
}