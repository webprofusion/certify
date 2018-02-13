using System;

namespace Certify.Core.Tests
{
    public class IntegrationTestBase
    {
        public string PrimaryTestDomain = "test.certifytheweb.com"; // TODO: get this from debug config as it changes per dev machine
        public string PrimaryIISRoot = @"c:\inetpub\wwwroot\";

        public IntegrationTestBase()
        {
            if (Environment.GetEnvironmentVariable("CERTIFYSSLDOMAIN") != null)
            {
                PrimaryTestDomain = Environment.GetEnvironmentVariable("CERTIFYSSLDOMAIN");
            }
        }
    }
}