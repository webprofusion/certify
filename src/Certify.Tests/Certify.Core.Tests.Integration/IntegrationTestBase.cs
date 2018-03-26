using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Certify.Core.Tests
{
    public class IntegrationTestBase
    {
        public string PrimaryTestDomain = "test.certifytheweb.com"; // TODO: get this from debug config as it changes per dev machine
        public string PrimaryIISRoot = @"c:\inetpub\wwwroot\";
        public Dictionary<string, string> ConfigSettings = new Dictionary<string, string>();

        public IntegrationTestBase()
        {
            if (Environment.GetEnvironmentVariable("CERTIFYSSLDOMAIN") != null)
            {
                PrimaryTestDomain = Environment.GetEnvironmentVariable("CERTIFYSSLDOMAIN");
            }

            /* ConfigSettings.Add("AWS_ZoneId", "example");
             ConfigSettings.Add("Azure_ZoneId", "example");
             ConfigSettings.Add("Cloudflare_ZoneId", "example");
             System.IO.File.WriteAllText("C:\\temp\\TestConfigSettings.json", JsonConvert.SerializeObject(ConfigSettings));
             */

            ConfigSettings = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText("C:\\temp\\TestConfigSettings.json"));
        }
    }
}