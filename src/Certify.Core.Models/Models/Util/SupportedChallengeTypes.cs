using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class SupportedChallengeTypes
    {
        public const string CHALLENGE_TYPE_HTTP = "http-01";
        public const string CHALLENGE_TYPE_SNI = "tls-sni-01";
    }
}