using System;
using System.Diagnostics;
using System.Linq;
using System.Web.Http;

namespace Certify.Service.Controllers
{
    public class ControllerBase : ApiController
    {
        public void DebugLog(string msg = null,
            [System.Runtime.CompilerServices.CallerMemberName] string callerName = "",
              [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
#if DEBUG
            if (!String.IsNullOrEmpty(sourceFilePath))
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