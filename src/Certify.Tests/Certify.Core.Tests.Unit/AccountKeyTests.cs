using System.IO;
using Certes;
using Certify.Management;
using Certify.Providers.ACME.Certes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class AccountKeyTests
    {
        private IKey newKey;

        [TestInitialize]
        public void CreateProblemKey()
        {
            var userAgent = Util.GetUserAgent();
            var certes = new CertesACMEProvider(Util.GetAppDataFolder() + "\\certes_test", userAgent);

            var keyFound = false;
            newKey = null;
            var attempts = 0;

            while (!keyFound)
            {
                var generator = GeneratorUtilities.GetKeyPairGenerator("ECDSA");
                var generatorParams = new ECKeyGenerationParameters(
                        CustomNamedCurves.GetOid("P-256"),
                        new SecureRandom()
                    );

                generator.Init(generatorParams);

                var keyPair = generator.GenerateKeyPair();

                var publicKey = (ECPublicKeyParameters)keyPair.Public;

                var xBytes = publicKey.Q.AffineXCoord.ToBigInteger().ToByteArrayUnsigned();
                var yBytes = publicKey.Q.AffineYCoord.ToBigInteger().ToByteArrayUnsigned();

                if (xBytes.Length != yBytes.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"Problem key found in {attempts} attempts");

                    keyFound = true;

                    var pem = "";
                    using (var sr = new StringWriter())
                    {
                        var pemWriter = new PemWriter(sr);
                        pemWriter.WriteObject(keyPair);
                        pem = sr.ToString();
                    }

                    System.Diagnostics.Debug.WriteLine($"{pem}");

                    newKey = KeyFactory.FromPem(pem);
                }
                attempts++;
            }

            //certes.InitProvider().Wait();

        }

        [TestMethod, Description("Identify problem key")]
        public void ProblemKey()
        {
            // found a problem key with invalid X/Y coord byte length
            Assert.IsNotNull(newKey);
            
        }

    }
}
