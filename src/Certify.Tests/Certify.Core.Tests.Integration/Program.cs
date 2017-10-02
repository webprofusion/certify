using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var r = new CertRequestTests();

            Task.Run(async () =>
            {
                await r.TestChallengeRequest();
                r.Dispose();
            });
        }
    }
}