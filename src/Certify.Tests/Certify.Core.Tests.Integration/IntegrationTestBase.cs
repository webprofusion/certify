using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    public class IntegrationTestBase
    {
        public static string PrimaryTestDomain = "test.certifytheweb.com"; // TODO: get this from debug config as it changes per dev machine
        public static string PrimaryIISRoot = @"c:\inetpub\wwwroot\";
    }
}