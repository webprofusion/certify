using ACMESharp.PKI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACMESharp.POSH.Util
{
    public static class PkiHelper
    {
        public static IPkiTool GetPkiTool(string name)
        {
            return string.IsNullOrEmpty(name)
                //? CertificateProvider.GetProvider()
                //: CertificateProvider.GetProvider(name);
                ? PkiToolExtManager.GetPkiTool()
                : PkiToolExtManager.GetPkiTool(name);
        }
    }
}
