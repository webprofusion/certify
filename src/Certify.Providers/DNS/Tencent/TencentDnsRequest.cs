using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace Certify.Providers.DNS.Tencent
{ 
    #region Models
    public enum RecordType
    {

        A,
        NS,
        MX,
        TXT,
        CNAME,
        SRV,
        AAAA,
        CAA,
        REDIRECT_URL,
        FORWARD_URL
    }

     

    #endregion
}
