using Certify.Models;
using Certify.Models.Config;

namespace Certify.Management
{
    /// <summary>
    /// Applies the instance's restriction on changes made locally (desktop app, CLI, local service API). Changes made
    /// through the management hub reach the instance by the hub connection and are not restricted.
    /// </summary>
    public static class LocalChangePolicy
    {
        public const string ReadOnlyMessage = "This instance is administered through the management hub, so this change can only be made through the hub.";
        public const string DeploymentTasksOnlyMessage = ReadOnlyMessage + " Locally, only the deployment tasks of existing managed certificates can be modified.";

        public static string GetRefusalMessage(LocalChangeRestriction restriction)
        {
            return restriction == LocalChangeRestriction.DeploymentTasksOnly ? DeploymentTasksOnlyMessage : ReadOnlyMessage;
        }

        /// <summary>
        /// Resolve a local add or update to the item which should be stored, or a failure if the change is not allowed
        /// </summary>
        /// <param name="stored">The currently stored item, or null if the update adds a new item</param>
        public static ActionResult<ManagedCertificate> ResolveManagedCertificateUpdate(LocalChangeRestriction restriction, ManagedCertificate stored, ManagedCertificate update)
        {
            if (restriction == LocalChangeRestriction.None)
            {
                return new ActionResult<ManagedCertificate>("OK", true, update);
            }

            if (restriction == LocalChangeRestriction.DeploymentTasksOnly && stored != null)
            {
                // only the deployment tasks are taken from the update, any other local change is not kept
                stored.PreRequestTasks = update.PreRequestTasks;
                stored.PostRequestTasks = update.PostRequestTasks;

                return new ActionResult<ManagedCertificate>("OK", true, stored);
            }

            return new ActionResult<ManagedCertificate>(GetRefusalMessage(restriction), false);
        }
    }
}
