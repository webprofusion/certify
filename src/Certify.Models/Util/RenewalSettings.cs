using System;
using System.Collections.Generic;
using Certify.Models.Config;

namespace Certify.Models
{
    [Flags]
    public enum RenewalMode
    {
        /// <summary>
        /// Renew items which are due to auto renew, auto decide 
        /// </summary>
        Auto = 0,
        /// <summary>
        /// Renewal all items which are due
        /// </summary>
        RenewalsDue = 1,
        /// <summary>
        /// Request/renew only items with a previous error status (ignore when last attempt was made)
        /// </summary>
        RenewalsWithErrors = 2,
        /// <summary>
        /// Request items which have not yet been requested (not previously renew or errored)
        /// </summary>
        NewItems = 4,
        /// <summary>
        /// Attempt to request/renew everything.
        /// </summary>
        All = 128
    }

    public class RenewalSettings
    {
        public DateTimeOffset? StartDate { get; set; }
        public DateTimeOffset? EndDate { get; set; }
        public RenewalMode Mode { get; set; }

        public bool IsPreviewMode { get; set; }

        public List<string> TargetManagedCertificates { get; set; } = new();

        public bool AwaitResults { get; set; } = true;
    }
    public class RenewalPrefs
    {
        public int RenewalIntervalDays { get; set; }
        public string RenewalIntervalMode { get; set; } = string.Empty;
        public int MaxRenewalRequests { get; set; }
        public bool IncludeStoppedSites { get; set; }

                /// <summary>
                ///  If true, don't send status UI messages for skipped items (items not due for renewal)
                /// </summary>
                public bool SuppressSkippedItems { get; set; }

                /// <summary>
                /// If true, perform batches of items in parallel
                /// </summary>
                public bool PerformParallelRenewals { get; set; }

                /// <summary>
                /// Collection of configured maintenance windows
                /// </summary>
                public List<MaintenanceWindow> MaintenanceWindows { get; set; } = new List<MaintenanceWindow>();

                /// <summary>
                /// If set, the ID of the default maintenance window for items that don't specify their own
                /// </summary>
                public string? DefaultMaintenanceWindowId { get; set; }

                /// <summary>
                /// Build the renewal preferences which apply to an instance from that instance's settings
                /// </summary>
                public static RenewalPrefs FromPreferences(Preferences prefs)
                {
                    if (prefs == null)
                    {
                        return new RenewalPrefs();
                    }

                    return new RenewalPrefs
                    {
                        RenewalIntervalDays = prefs.RenewalIntervalDays,
                        RenewalIntervalMode = prefs.RenewalIntervalMode ?? RenewalIntervalModes.PercentageLifetime,
                        MaxRenewalRequests = prefs.MaxRenewalRequests,
                        IncludeStoppedSites = !prefs.IgnoreStoppedSites,
                        PerformParallelRenewals = prefs.EnableParallelRenewals,
                        MaintenanceWindows = prefs.MaintenanceWindows ?? [],
                        DefaultMaintenanceWindowId = prefs.DefaultMaintenanceWindowId
                    };
                }
            }
        }
