using Certify.Models;
using System.Collections.Generic;
using System.Web.Http;

namespace Certify.Service
{
    public class SystemController : ApiController
    {
        private Management.CertifyManager _certifyManager = new Certify.Management.CertifyManager();

        public string GetAppVersion()
        {
            return new Management.Util().GetAppVersion().ToString();
        }
    }
}