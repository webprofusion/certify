using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    [ImplementPropertyChanged]
    public class DomainOption
    {
        public bool IsChanged { get; set; }

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
    }
}