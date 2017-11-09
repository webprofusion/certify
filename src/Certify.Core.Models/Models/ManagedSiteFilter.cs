using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class ManagedSiteFilter
    {
        // optional keyword to filter name or domains
        public string Keyword { get; set; }

        // filter results to just those sites which will be included in the next auto renewal
        public bool IncludeOnlyNextAutoRenew { get; set; }

        public int MaxResults { get; set; } = 0;
    }
}