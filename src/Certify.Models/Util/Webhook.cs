using System.Collections.Generic;

namespace Certify.Models.Utils
{
    public partial class Webhook
    {
        public const string ON_NONE = "None";
        public const string ON_SUCCESS = "On Success";
        public const string ON_ERROR = "On Error";
        public const string ON_SUCCESS_OR_ERROR = "On Success Or Error";

        public const string METHOD_GET = "GET";
        public const string METHOD_POST = "POST";

        public const string DEFAULT_BODY = @"{
          ""Success"": ""$Success"",
          ""PrimaryDomain"": ""$PrimaryDomain"",
          ""SANs"": ""$SubjectAlternativeNames"",
          ""ChallengeType"": ""$ChallengeType""
        }";

        public static readonly List<string> TriggerTypes = new List<string>() { ON_NONE, ON_SUCCESS, ON_ERROR, ON_SUCCESS_OR_ERROR };
        public static readonly List<string> Methods = new List<string>() { METHOD_GET, METHOD_POST };
    }
}
