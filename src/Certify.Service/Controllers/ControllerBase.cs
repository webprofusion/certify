using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace Certify.Service.Controllers
{
    public class CustomAuthCheckAttribute : AuthorizeAttribute
    {
        protected override bool IsAuthorized(HttpActionContext actionContext)
        {
            var user = actionContext.RequestContext.Principal as System.Security.Principal.WindowsPrincipal;
            if (user.IsInRole(WindowsBuiltInRole.Administrator)) return true;
            if (user.IsInRole(WindowsBuiltInRole.PowerUser)) return true;

            return base.IsAuthorized(actionContext);
        }
    }

    [CustomAuthCheck]
    public class ControllerBase : ApiController
    {
        public void DebugLog(string msg = null,
            [System.Runtime.CompilerServices.CallerMemberName] string callerName = "",
              [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
#if DEBUG
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                sourceFilePath = System.IO.Path.GetFileName(sourceFilePath);
            }
            var output = $"API [{sourceFilePath}/{callerName}] {msg}";

            Console.ForegroundColor = ConsoleColor.Yellow;
            Debug.WriteLine(output);
            Console.ForegroundColor = ConsoleColor.White;
#endif
        }
    }
}