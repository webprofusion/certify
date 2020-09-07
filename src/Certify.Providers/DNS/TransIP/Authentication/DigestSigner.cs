using System;
using System.IO;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace Certify.Providers.DNS.TransIP.Authentication {
	internal class DigestSigner {
		public string Sign(byte[] data, string privateKey) 
        {
            var pem = GetPem(privateKey);
            var parameters = GetCipherParameters(pem);
            var signature = Encrypt(data, parameters);
			return Convert.ToBase64String(signature);
		}

        private static object GetPem(string key)
        {
            var keyReader = new StringReader(key);
            var pemReader = new PemReader(keyReader);
            return pemReader.ReadObject();
        }

        private static ICipherParameters GetCipherParameters(object pem)
        {
            switch (pem)
            {
                case RsaPrivateCrtKeyParameters parameters:
                    return parameters;
                case AsymmetricCipherKeyPair keyPair:
                    return keyPair.Private;
                default:
                    throw new NotImplementedException($"Error getting cipher parameters. '{pem.GetType()}' is not supported.");
            }
        }

		private static byte[] Encrypt(byte[] digest, ICipherParameters parameters)
        {
            var cipher = CipherUtilities.GetCipher("RSA/None/PKCS1Padding");
            cipher.Init(true, parameters);
            return cipher.DoFinal(digest);
        }
    }
}
