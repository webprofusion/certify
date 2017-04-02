using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    [ImplementPropertyChanged]
    public class ContactRegistration
    {
        public string EmailAddress { get; set; }
        public bool AgreedToTermsAndConditions { get; set; }
    }
}