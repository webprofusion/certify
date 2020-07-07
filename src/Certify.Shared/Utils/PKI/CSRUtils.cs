using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Pkcs;

namespace Certify.Shared.Core.Utils.PKI
{
    public class CSRUtils
    {
        /// <summary>
        /// Convert a PEM format string (Base64 with headers) to bytes
        /// </summary>
        /// <param name="pem"></param>
        /// <returns></returns>
        public static byte[] GetBytesFromPEM(string pem)
        {
            var pemString = string.Join("",
                              pem.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(s => !s.Contains("BEGIN ") && !s.Contains("END "))
                              .ToArray()
                             );

            return Convert.FromBase64String(pemString);
        }

        /// <summary>
        /// Decode a CSR from PEM 
        /// </summary>
        /// <param name="pem"></param>
        /// <returns></returns>
        public static Pkcs10CertificationRequest DecodeCSR(string pem)
        {
            var bytes = GetBytesFromPEM(pem);
            return DecodeCSR(bytes);
        }

        /// <summary>
        /// Decode a CSR from bytes
        /// </summary>
        /// <param name="csr"></param>
        /// <returns></returns>
        public static Pkcs10CertificationRequest DecodeCSR(byte[] csr)
        {
            return new Pkcs10CertificationRequest(csr);
        }
    }
}
