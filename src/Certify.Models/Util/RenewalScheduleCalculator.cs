using System;
using System.Linq;
using Certify.Models.Config;

namespace Certify.Models
{
    /// <summary>
    /// Shared calculation of when a managed certificate will next be renewed, and the reason for that decision.
    /// The renewal process uses this to decide what to attempt, and the UIs use it to report the plan to the user,
    /// so both describe renewal the same way.
    /// </summary>
    public static class RenewalScheduleCalculator
    {
        /// <summary>
        /// Calculate the next renewal attempt for an item using the given renewal preferences, including any
        /// maintenance window which constrains when the attempt can take place.
        /// </summary>
        /// <param name="item">The managed certificate to evaluate</param>
        /// <param name="prefs">Renewal preferences for the instance which owns the item</param>
        /// <param name="testDateTime">The current time to evaluate against (defaults to now)</param>
        public static RenewalDueInfo? CalculateNextRenewalAttempt(ManagedCertificate item, RenewalPrefs prefs, DateTimeOffset? testDateTime = null)
        {
            if (item == null || prefs == null)
            {
                return null;
            }

            var dueInfo = ManagedCertificate.CalculateNextRenewalAttempt(
                item,
                prefs.RenewalIntervalDays,
                string.IsNullOrEmpty(prefs.RenewalIntervalMode) ? RenewalIntervalModes.PercentageLifetime : prefs.RenewalIntervalMode,
                testDateTime);

            if (dueInfo == null)
            {
                return null;
            }

            ApplyMaintenanceWindow(item, prefs, dueInfo, testDateTime);

            return dueInfo;
        }

        /// <summary>
        /// Get the maintenance window which applies to an item, being the window assigned to the item or otherwise the
        /// configured default window. Returns null if no enabled window applies.
        /// </summary>
        public static MaintenanceWindow? GetApplicableMaintenanceWindow(ManagedCertificate item, RenewalPrefs prefs)
        {
            if (prefs?.MaintenanceWindows == null || !prefs.MaintenanceWindows.Any())
            {
                return null;
            }

            var windowId = item?.MaintenanceWindowId ?? prefs.DefaultMaintenanceWindowId;

            if (string.IsNullOrEmpty(windowId))
            {
                return null;
            }

            var window = prefs.MaintenanceWindows.FirstOrDefault(w => w.Id == windowId);

            // a window which was deleted or disabled since it was assigned does not constrain renewal
            return window?.IsEnabled == true ? window : null;
        }

        /// <summary>
        /// Checks if the given time is within the maintenance window which applies to a managed certificate
        /// </summary>
        /// <param name="item">The managed certificate to check</param>
        /// <param name="prefs">Renewal preferences containing maintenance windows configuration</param>
        /// <param name="currentTime">The current time to check against (defaults to now if not specified)</param>
        /// <returns>Tuple containing: isWithinWindow (bool), reason (string)</returns>
        public static (bool IsWithinWindow, string Reason) IsWithinMaintenanceWindow(
            ManagedCertificate item,
            RenewalPrefs prefs,
            DateTimeOffset? currentTime = null)
        {
            var checkTime = currentTime ?? DateTimeOffset.Now;

            // If no maintenance windows are configured, renewal is always allowed
            if (prefs.MaintenanceWindows == null || !prefs.MaintenanceWindows.Any())
            {
                return (true, "No maintenance windows configured - renewal allowed anytime");
            }

            // Determine which maintenance window to use for this item
            var windowId = item.MaintenanceWindowId ?? prefs.DefaultMaintenanceWindowId;

            // If no window is specified for this item and no default is set, renewal is allowed
            if (string.IsNullOrEmpty(windowId))
            {
                return (true, "No maintenance window assigned - renewal allowed anytime");
            }

            // Find the maintenance window
            var window = prefs.MaintenanceWindows.FirstOrDefault(w => w.Id == windowId);

            if (window == null)
            {
                // Window ID is set but window not found - allow renewal (could be deleted window)
                return (true, $"Configured maintenance window '{windowId}' not found - renewal allowed");
            }

            if (!window.IsEnabled)
            {
                return (true, $"Maintenance window '{window.Name}' is disabled - renewal allowed anytime");
            }

            // Check if we're within the window
            if (window.IsWithinWindow(checkTime))
            {
                return (true, $"Within maintenance window '{window.Name}' ({window.GetScheduleDescription()})");
            }
            else
            {
                var nextOccurrence = window.GetNextOccurrence(checkTime);
                var nextTimeStr = nextOccurrence.HasValue
                    ? nextOccurrence.Value.ToString("g")
                    : "unknown";

                return (false, $"Limited to Maintenance Window '{window.Name}' ({window.GetScheduleDescription()}). Next window: {nextTimeStr}");
            }
        }

        /// <summary>
        /// Adjust a renewal due result for the maintenance window which applies to the item. An attempt cannot be made
        /// outside the window, so a renewal which is due right now is deferred until the window next opens, and a renewal
        /// planned for a future date is planned for the first window occurrence on or after that date.
        /// </summary>
        private static void ApplyMaintenanceWindow(ManagedCertificate item, RenewalPrefs prefs, RenewalDueInfo dueInfo, DateTimeOffset? testDateTime)
        {
            var window = GetApplicableMaintenanceWindow(item, prefs);

            if (window == null)
            {
                return;
            }

            var checkTime = testDateTime ?? DateTimeOffset.Now;

            // an attempt made now would fall outside the window, so renewal is currently deferred. This applies whether or not
            // the interval says renewal is due, because a caller may require renewal for its own reasons (e.g. a subscription update)
            if (!window.IsWithinWindow(checkTime))
            {
                dueInfo.IsDeferredByMaintenanceWindow = true;
            }

            if (dueInfo.IsRenewalDue)
            {
                if (dueInfo.IsDeferredByMaintenanceWindow)
                {
                    dueInfo.Reason = IsWithinMaintenanceWindow(item, prefs, checkTime).Reason;
                    dueInfo.DateNextRenewalAttempt = window.GetNextOccurrence(checkTime) ?? dueInfo.DateNextRenewalAttempt;
                }

                return;
            }

            // renewal is not yet due, so the planned date moves forward to the first window occurrence on or after it
            if (dueInfo.DateNextRenewalAttempt.HasValue && !window.IsWithinWindow(dueInfo.DateNextRenewalAttempt.Value))
            {
                var nextOccurrence = window.GetNextOccurrence(dueInfo.DateNextRenewalAttempt.Value);

                if (nextOccurrence.HasValue)
                {
                    dueInfo.DateNextRenewalAttempt = nextOccurrence;
                    dueInfo.Reason = $"{dueInfo.Reason.Trim()} The attempt will take place in maintenance window '{window.Name}' ({window.GetScheduleDescription()}).";
                }
            }
        }
    }
}
