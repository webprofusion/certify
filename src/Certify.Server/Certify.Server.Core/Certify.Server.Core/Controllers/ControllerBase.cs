using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    public class CustomAuthCheckAttribute : AuthorizeAttribute
    {
        /* protected override bool IsAuthorized(HttpActionContext actionContext)
         {
 #if DEBUG_NO_AUTH
     return true;
 #endif
             var user = actionContext.RequestContext.Principal as System.Security.Principal.WindowsPrincipal;
             if (user.IsInRole(WindowsBuiltInRole.Administrator))
             {
                 return true;
             }

             if (user.IsInRole(WindowsBuiltInRole.PowerUser))
             {
                 return true;
             }

             return false;
         }*/
    }

    [ApiController]
    //   [CustomAuthCheck]
    public class ControllerBase : Controller
    {
        internal void DebugLog(string msg = null,
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

        Client.AuthContext _currentAuthContext = null;

        /// <summary>
        /// Set the current auth context for the current request, only used internally when invoking controller outside of an http request
        /// </summary>
        /// <param name="authContext"></param>
        /// 
        [NonAction]
        public void SetCurrentAuthContext(Client.AuthContext authContext)
        {
            _currentAuthContext = authContext;
        }

        [NonAction]
        public string GetContextUserId()
        {
            if (_currentAuthContext != null)
            {
                return _currentAuthContext.UserId;
            }

            // TODO: sign passed value provided by public API using public APIs access token
            var contextUserId = Request?.Headers["X-Context-User-Id"];

            return contextUserId;
        }
    }
}
