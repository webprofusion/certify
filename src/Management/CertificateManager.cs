using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class CertificateManager
    {
        public X509Certificate GetCertificate(string filename)
        {
            X509Certificate cert = new X509Certificate();
            cert.Import(filename);
            return cert;
        }
    }
}