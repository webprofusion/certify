using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Certify.Models
{
    public class WebhookResult
    {
        public WebhookResult(bool success, int statusCode)
        {
            Success = success;
            StatusCode = statusCode;
        }

        public bool Success = false;

        public int StatusCode = 0;
    }
}
