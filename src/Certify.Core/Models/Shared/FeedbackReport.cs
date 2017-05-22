using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models.Shared
{
    public class FeedbackReport
    {
        public string EmailAddress { get; set; }
        public string Comment { get; set; }
        public object SupportingData { get; set; }
    }
}