using Certify.Management;
using Certify.Models;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Certify.Service.Controllers
{
    /// <summary>
    /// Refuses a change requested through the local service API while the instance restricts local changes because it is
    /// administered through the management hub. Filters only run for http requests, so controllers the hub invokes
    /// in-process are not restricted.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RestrictedLocalChangeAttribute : Attribute, IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var restriction = CoreAppSettings.Current.LocalChangeRestriction;

            if (restriction != LocalChangeRestriction.None)
            {
                context.Result = ControllerBase.LocalChangeRefused(LocalChangePolicy.GetRefusalMessage(restriction));
            }
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
