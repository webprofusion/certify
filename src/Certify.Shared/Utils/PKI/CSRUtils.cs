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
            var identifiers = new List<string>();

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

                var allNames = names
                    .GetNames()
                    .Select(n => n.Name.ToString())
                    .ToList();

                identifiers.AddRange(allNames);
            }

            // check primary subject
            var oids = requestInfo.Subject.GetOidList();

            var subjectName = "";

            foreach (DerObjectIdentifier o in oids)
            {
                if (o.Id == X509ObjectIdentifiers.CommonName.Id)
                {
                    subjectName = requestInfo.Subject.GetValueList()[oids.IndexOf(o)].ToString();
                    break;
                }
            }

            if (subjectName != null)
            {
                if (!identifiers.Contains(subjectName))
                {
                    identifiers.Insert(0, subjectName);
                }
            }

            return identifiers;

        }

        /// <summary>
        /// Get the subject fields of a CSR (e.g. O, OU, L, ST, C) as a distinguished name, excluding the Common Name
        /// </summary>
        /// <param name="csr">PEM encoded CSR</param>
        /// <returns>The distinguished name, or null if the subject has no other fields</returns>
        public static string GetCsrSubjectFields(string csr)
        {
            var fields = GetCsrSubjectAttributes(csr, includeCommonName: false);

            if (!fields.Any())
            {
                return null;
            }

            return new X509Name(fields.Select(f => f.oid).ToList(), fields.Select(f => f.value).ToList()).ToString();
        }

        private static readonly Dictionary<string, string> _subjectFieldTitles = new Dictionary<string, string>
        {
            ["CN"] = "Common Name",
            ["O"] = "Organization",
            ["OU"] = "Organizational Unit",
            ["L"] = "Locality",
            ["ST"] = "State or Province",
            ["C"] = "Country",
            ["E"] = "Email Address",
            ["STREET"] = "Street",
            ["PostalCode"] = "Postal Code",
            ["SERIALNUMBER"] = "Serial Number"
        };

        /// <summary>
        /// Get the subject fields of a CSR with a display title for each (e.g. "Organization (O)")
        /// </summary>
        /// <param name="csr">PEM encoded CSR</param>
        /// <param name="includeCommonName">If false, the Common Name is left out</param>
        public static List<KeyValuePair<string, string>> DescribeCsrSubjectFields(string csr, bool includeCommonName)
        {
            return GetCsrSubjectAttributes(csr, includeCommonName)
                .Select(f =>
                {
                    var symbol = X509Name.DefaultSymbols.TryGetValue(f.oid, out var s) ? s : f.oid.Id;
                    var title = _subjectFieldTitles.TryGetValue(symbol, out var t) ? $"{t} ({symbol})" : symbol;
                    return new KeyValuePair<string, string>(title, f.value);
                })
                .ToList();
        }

        private static List<(DerObjectIdentifier oid, string value)> GetCsrSubjectAttributes(string csr, bool includeCommonName)
        {
            var subject = DecodeCsr(csr).GetCertificationRequestInfo().Subject;

            var oids = subject.GetOidList();
            var values = subject.GetValueList();

            return oids
                .Select((oid, i) => (oid, value: values[i]))
                .Where(f => includeCommonName || !f.oid.Equals(X509Name.CN))
                .ToList();
        }

        private static T GetAsn1ObjectRecursive<T>(DerSequence sequence, string id) where T : Asn1Object
        {
            if (sequence.OfType<DerObjectIdentifier>().Any(o => o.Id == id))
            {
                return sequence.OfType<T>().First();
            }

            foreach (var subSequence in sequence.OfType<DerSequence>())
            {
                var value = GetAsn1ObjectRecursive<T>(subSequence, id);
                if (value != default(T))
                {
                    return value;
                }
            }

            return default;
        }

        public static bool CanParsePrivateKey(string keyContent)
        {
            using var keyReader = new StringReader(keyContent);
            using var pemReader = new Org.BouncyCastle.OpenSsl.PemReader(keyReader);

            var readObject = pemReader.ReadObject();

            if (readObject is AsymmetricCipherKeyPair keyPair)
            {
                return keyPair.Private.IsPrivate;
            }
            else if (readObject is AsymmetricKeyParameter keyParameter)
            {
                return keyParameter.IsPrivate;
            }
            else
            {
                return false;
            }
        }
    }
}
