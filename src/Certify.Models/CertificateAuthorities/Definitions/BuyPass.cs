using System;
using System.Collections.Generic;
using System.Text;
using Certify.Models;

namespace Certify.CertificateAuthorities.Definitions
{
    internal class BuyPass
    {
        public static CertificateAuthority GetDefinition()
        {
            var certs = GetKnownCertificates();

            return new CertificateAuthority
            {
                Id = "buypass.com",
                Title = "Buypass Go SSL",
                Description = "Buypass Go SSL is a free SSL certificate service from Buypass CA using the Buypass ACME API. Certificates are valid for 180 days and can contain up to 5 domains per certificate (wildcards are not available)",
                APIType = CertAuthorityAPIType.ACME_V2.ToString(),
                WebsiteUrl = "https://www.buypass.com/",
                PrivacyPolicyUrl = "https://www.buypass.com/about-buypass/privacy-policy",
                ProductionAPIEndpoint = "https://api.buypass.com/acme/directory",
                StagingAPIEndpoint = "https://api.test4.buypass.no/acme/directory",
                IsEnabled = true,
                IsCustom = false,
                SANLimit = 5,
                StandardExpiryDays = 180,
                RequiresEmailAddress = true,
                SupportedFeatures = new List<string> {
                    CertAuthoritySupportedRequests.DOMAIN_SINGLE.ToString(),
                    CertAuthoritySupportedRequests.DOMAIN_MULTIPLE_SAN.ToString()
                },
                SupportedKeyTypes = new List<string> {
                    StandardKeyTypes.RSA256,
                    StandardKeyTypes.RSA256_3072,
                    StandardKeyTypes.RSA256_4096,
                    StandardKeyTypes.ECDSA256,
                    StandardKeyTypes.ECDSA384
                },
                TrustedRoots = new Dictionary<string, string>{
                    { 
                        // Buypass Class 2 Root CA
                        "490a7574de870a47fe58eef6c76bebc60b124099",
                        certs["490a7574de870a47fe58eef6c76bebc60b124099"]
                    }
                }
            };
        }

        public static Dictionary<string, string> GetKnownCertificates()
        {
            var knownCerts = new Dictionary<string, string>
            {
                {
                    // Buypass Class 2 Root CA
                    "490a7574de870a47fe58eef6c76bebc60b124099",
@"-----BEGIN CERTIFICATE-----
MIIFWTCCA0GgAwIBAgIBAjANBgkqhkiG9w0BAQsFADBOMQswCQYDVQQGEwJOTzEd
MBsGA1UECgwUQnV5cGFzcyBBUy05ODMxNjMzMjcxIDAeBgNVBAMMF0J1eXBhc3Mg
Q2xhc3MgMiBSb290IENBMB4XDTEwMTAyNjA4MzgwM1oXDTQwMTAyNjA4MzgwM1ow
TjELMAkGA1UEBhMCTk8xHTAbBgNVBAoMFEJ1eXBhc3MgQVMtOTgzMTYzMzI3MSAw
HgYDVQQDDBdCdXlwYXNzIENsYXNzIDIgUm9vdCBDQTCCAiIwDQYJKoZIhvcNAQEB
BQADggIPADCCAgoCggIBANfHXvfBB9R3+0Mh9PT1aeTuMgHbo4Yf5FkNuud1g1Lr
6hxhFUi7HQfKjK6w3Jad6sNgkoaCKHOcVgb/S2TwDCo3SbXlzwx87vFKu3MwZfPV
L4O2fuPn9Z6rYPnT8Z2SdIrkHJasW4DptfQxh6NR/Md+oW+OU3fUl8FVM5I+GC91
1K2GScuVr1QGbNgGE41b/+EmGVnAJLqBcXmQRFBoJJRfuLMR8SlBYaNByyM21cHx
MlAQTn/0hpPshNOOvEu/XAFOBz3cFIqUCqTqc/sLUegTBxj6DvEr0VQVfTzh97QZ
QmdiXnfgolXsttlpF9U6r0TtSsWe5HonfOV116rLJeffawrbD02TTqigzXsu8lkB
arcNuAeBfos4GzjmCleZPe4h6KP1DBbdi+w0jpwqHAAVF41og9JwnxgIzRFo1clr
Us3ERo/ctfPYV3Me6ZQ5BL/T3jjetFPsaRyifsSP5BtwrfKi+fv3FmRmaZ9JUaLi
FRhnBkp/1Wy1TbMz4GHrXb7pmA8y1x1LPC5aAVKRCfLf6o3YBkBjqhHk/sM3nhRS
P/TizPJhk9H9Z2vXUq6/aKtAQ6BXNVN48FP4YUIHZMbXb5tMOA1jrGKvNouicwoN
9SG9dKpN6nIDSdvHXx1iY8f93ZHsM+71bbRuMGjeyNYmsHVee7QHIJihdjK4TWxP
AgMBAAGjQjBAMA8GA1UdEwEB/wQFMAMBAf8wHQYDVR0OBBYEFMmAd+BikoL1Rpzz
uvdMw964o605MA4GA1UdDwEB/wQEAwIBBjANBgkqhkiG9w0BAQsFAAOCAgEAU18h
9bqwOlI5LJKwbADJ784g7wbylp7ppHR/ehb8t/W2+xUbP6umwHJdELFx7rxP462s
A20ucS6vxOOto70MEae0/0qyexAQH6dXQbLArvQsWdZHEIjzIVEpMMpghq9Gqx3t
OluwlN5E40EIosHsHdb9T7bWR9AUC8rmyrV7d35BH16Dx7aMOZawP5aBQW9gkOLo
+fsicdl9sz1Gv7SEr5AcD48Saq/v7h56rgJKihcrdv6sVIkkLE8/trKnToyokZf7
KcZ7XC25y2a2t6hbElGFtQl+Ynhw/qlqYLYdDnkM/crqJIByw5c/8nerQyIKx+u2
DISCLIBrQYoIwOula9+ZEsuK1V6ADJHgJgg2SMX6OBE1/yWDLfJ6v9r9jv6ly0Us
H8SIU653DtmadsWOLB2jutXsMq7Aqqz30XpN69QH4kj3Io6wpJ9qzo6ysmD0oyLQ
I+uUWnpp3Q+/QFesa1lQ2aOZ4W7+jQF5JyMV3pKdewlNWudLSDBaGOYKbeaP4NK7
5t98biGCwWg5TbSYWGZizEqQXsP6JwSxeRV0mcy+rSDeJmAc61ZRpqPq5KM/p/9h
3PFaTWwyI0PurKju7koSCTxdccK+efrCh2gdC/1cacwG0Jp9VJkqyTkaGa9LKkPz
Y11aWOIv4x3kqdbQCtCev9eBCfHJxyYNrJgWVqA=
-----END CERTIFICATE-----
"
                }
            };
            return knownCerts;
        }
    }
}
