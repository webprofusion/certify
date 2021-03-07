using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Utilities.IO.Pem;

namespace Certify.Shared.Core.Utils.PKI
{
    public class CSRUtils
    {
        /// <summary>
        /// Convert a PEM format string (Base64 with headers) to bytes
        /// </summary>
        /// <param name="pem"></param>
        /// <returns></returns>
        public static byte[] GetBytesFromPem(string pem)
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
        public static Pkcs10CertificationRequest DecodeCsr(string pem)
        {
            var bytes = GetBytesFromPem(pem);
            return DecodeCsr(bytes);
        }

        /// <summary>
        /// Decode a CSR from bytes
        /// </summary>
        /// <param name="csr"></param>
        /// <returns></returns>
        public static Pkcs10CertificationRequest DecodeCsr(byte[] csr)
        {
            return new Pkcs10CertificationRequest(csr);
        }

        public static List<string> DecodeCsrSubjects(string csr)
        {
            var csrBytes = GetBytesFromPem(csr);
            return DecodeCsrSubjects(csrBytes);
        }

        public static List<string> DecodeCsrSubjects(byte[] csrBytes)
        {
            // based on https://stackoverflow.com/a/45424266 by https://stackoverflow.com/users/814735/cyril-durand

            var pem = new PemObject("CSR", csrBytes);
            var request = new Pkcs10CertificationRequest(pem.Content);
            var requestInfo = request.GetCertificationRequestInfo();

            // an Attribute is a collection of Sequence which contains a collection of Asn1Object
            // let's find the sequence that contains a DerObjectIdentifier with Id of "1.2.840.113549.1.9.14"
            var extensionSequence = requestInfo.Attributes.OfType<DerSequence>()
                                                                  .FirstOrDefault(o => o.OfType<DerObjectIdentifier>()
                                                                               .Any(oo => oo.Id == PkcsObjectIdentifiers.Pkcs9AtExtensionRequest.Id)); // pkcs-9/extensionRequest,  "1.2.840.113549.1.9.14"

            // let's get the set of value for this sequence
            var extensionSet = extensionSequence?.OfType<DerSet>().First();

            var str = extensionSet != null ?
                GetAsn1ObjectRecursive<DerOctetString>(extensionSet.OfType<DerSequence>().First(), X509Extensions.SubjectAlternativeName.Id)
                : null;

            if (str != null)
            {
                //subject alternative names
                var names = GeneralNames.GetInstance(Asn1Object.FromByteArray(str.GetOctets()));

                return names
                    .GetNames()
                    .Select(n => n.Name.ToString())
                    .ToList();
            }
            else
            {
                var oids = requestInfo.Subject.GetOidList();

                string subjectName = "";

                foreach (DerObjectIdentifier o in oids)
                {
                    if (o.Id == X509ObjectIdentifiers.CommonName.Id)
                    {
                        subjectName = requestInfo.Subject.GetValueList()[oids.IndexOf(o)].ToString();
                        break;
                    }
                }

                // we just have a single subject
                return new List<string>
                {
                   subjectName
                };
            }
        }

        static T GetAsn1ObjectRecursive<T>(DerSequence sequence, String id) where T : Asn1Object
        {
            if (sequence.OfType<DerObjectIdentifier>().Any(o => o.Id == id))
            {
                return sequence.OfType<T>().First();
            }

            foreach (DerSequence subSequence in sequence.OfType<DerSequence>())
            {
                T value = GetAsn1ObjectRecursive<T>(subSequence, id);
                if (value != default(T))
                {
                    return value;
                }
            }

            return default(T);
        }

        public static bool CanParsePrivateKey(string keyContent)
        {
            using (var keyReader = new StringReader(keyContent))
            {
                var readKeyPair = (AsymmetricCipherKeyPair)new Org.BouncyCastle.OpenSsl.PemReader(keyReader).ReadObject();
                if (readKeyPair.Private.IsPrivate)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

        }
    }
}
