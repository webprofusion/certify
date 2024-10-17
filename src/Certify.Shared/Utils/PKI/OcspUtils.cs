using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models.Certify.Models;
using Certify.Models.Providers;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.X509;

namespace Certify.Shared.Utils
{
    public class OcspUtils
    {
        // adapted from https://github.com/reisjr/BouncyCastleExamples/blob/master/OcspClient/OcspClient.cs by https://github.com/reisjr
        private static void LogOrConsole(ILog log, string msg)
        {
            if (log != null)
            {
                log.Verbose(msg);
            }
            else
            {
                Console.WriteLine(msg);
            }
        }
        public static async Task<CertificateStatusType> Query(X509Certificate endEntityCert, X509Certificate issuerCert, ILog log = null)
        {
            // Query the first Ocsp Url found in certificate
            var urls = GetAuthorityInformationAccessOcspUrl(endEntityCert);

            if (urls?.Any() != true)
            {
                return CertificateStatusType.OcspNotSupported;
            }

            var url = urls[0];

            LogOrConsole(log, "Querying '" + url + "'...");

            var req = GenerateOcspRequest(issuerCert, endEntityCert.SerialNumber);

            var data = req.GetEncoded();

            using (var client = new HttpClient())
            {

                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/ocsp-response"));

                var content = new ByteArrayContent(data);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/ocsp-request");
                // request.ServicePoint.Expect100Continue = false;

                var response = await client.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    return ProcessOcspResponse(endEntityCert, issuerCert, bytes);
                }
                else
                {
                    LogOrConsole(log, "Error: " + response.StatusCode);
                    return CertificateStatusType.Unknown;
                }
            }
        }

        private static CertificateStatusType ProcessOcspResponse(X509Certificate eeCert, X509Certificate issuerCert, byte[] binaryResp)
        {
            var r = new OcspResp(binaryResp);
            var cStatus = CertificateStatusType.Unknown;

            switch (r.Status)
            {
                case OcspRespStatus.Successful:
                    var or = (BasicOcspResp)r.GetResponseObject();

                    if (or.Responses.Length == 1)
                    {
                        var resp = or.Responses[0];

                        if (!IsValidCertificateId(issuerCert, eeCert, resp.GetCertID()))
                        {
                            return CertificateStatusType.InvalidCertId;
                        }

                        var certificateStatus = resp.GetCertStatus();

                        if (certificateStatus == Org.BouncyCastle.Ocsp.CertificateStatus.Good)
                        {
                            cStatus = CertificateStatusType.Active;
                        }
                        else if (certificateStatus is Org.BouncyCastle.Ocsp.RevokedStatus)
                        {
                            cStatus = CertificateStatusType.Revoked;
                        }
                        else if (certificateStatus is Org.BouncyCastle.Ocsp.UnknownStatus)
                        {
                            cStatus = CertificateStatusType.Unknown;
                        }
                    }

                    break;
                case OcspRespStatus.Unauthorized:
                    cStatus = CertificateStatusType.Unknown;
                    break;
                case OcspRespStatus.TryLater:
                    cStatus = CertificateStatusType.TryLater;
                    break;
                default:
                    cStatus = CertificateStatusType.Unknown;
                    break;
            }

            return cStatus;
        }

        /// <summary>
        /// The certificate response matches the request
        /// </summary>
        /// <param name="issuerCert"></param>
        /// <param name="eeCert"></param>
        /// <param name="certificateId"></param>
        private static bool IsValidCertificateId(X509Certificate issuerCert, X509Certificate eeCert, CertificateID certificateId)
        {
            var expectedId = new CertificateID(CertificateID.DigestSha1, issuerCert, eeCert.SerialNumber);

            if (!expectedId.SerialNumber.Equals(certificateId.SerialNumber))
            {
                // Invalid certificate ID in response
                return false;
            }

            if (!Org.BouncyCastle.Utilities.Arrays.AreEqual(expectedId.GetIssuerNameHash(), certificateId.GetIssuerNameHash()))
            {
                // Invalid certificate Issuer in response
                return false;
            }

            return true;
        }

        private static OcspReq GenerateOcspRequest(X509Certificate issuerCert, BigInteger serialNumber)
        {
            var ocspRequestGenerator = new OcspReqGenerator();

            var id = new CertificateID(CertificateID.DigestSha1, issuerCert, serialNumber);

            ocspRequestGenerator.AddRequest(id);

            var oids = new List<DerObjectIdentifier>();
            var values = new Dictionary<DerObjectIdentifier, X509Extension>();

            oids.Add(OcspObjectIdentifiers.PkixOcsp);

            Asn1OctetString asn1 = new DerOctetString(new DerOctetString(new byte[] { 1, 3, 6, 1, 5, 5, 7, 48, 1, 1 }));

            values.Add(OcspObjectIdentifiers.PkixOcsp, new X509Extension(false, asn1));
            ocspRequestGenerator.SetRequestExtensions(new X509Extensions(oids, values));

            return ocspRequestGenerator.Generate();
        }

        public static List<string> GetAuthorityInformationAccessOcspUrl(X509Certificate cert)
        {
            var ocspUrls = new List<string>();

            try
            {
                var asn1 = GetExtensionValue(cert, X509Extensions.AuthorityInfoAccess.Id);

                if (asn1 == null)
                {
                    return null;
                }

                var aia = AuthorityInformationAccess.GetInstance(asn1);
                var desc = aia.GetAccessDescriptions();
                var ocspUrl = desc.First(a => a.AccessMethod.Id == "1.3.6.1.5.5.7.48.1").AccessLocation;

                ocspUrls.Add(ocspUrl.Name.ToString());

            }
            catch (Exception e)
            {
                throw new Exception("Error parsing AuthorityInformationAccess.", e);
            }

            return ocspUrls;
        }

        protected static Asn1Object GetExtensionValue(X509Certificate cert, string oid)
        {
            if (cert == null)
            {
                return null;
            }

            var bytes = cert.GetExtensionValue(new DerObjectIdentifier(oid))?.GetOctets();

            if (bytes == null)
            {
                return null;
            }

            using (var aIn = new Asn1InputStream(bytes))
            {
                return aIn.ReadObject();
            }
        }
    }
}
