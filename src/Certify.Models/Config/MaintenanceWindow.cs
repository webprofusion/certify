using System;
using System.Collections.Generic;
using System.Globalization;

namespace Certify.Models.Config
{
    /// <summary>
    /// Represents a day of the week for maintenance window scheduling
    /// </summary>
    [Flags]
    public enum MaintenanceDays
    {
        None = 0,
        Sunday = 1,
        Monday = 2,
        Tuesday = 4,
        Wednesday = 8,
        Thursday = 16,
        Friday = 32,
        Saturday = 64,
        Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
        Weekends = Saturday | Sunday,
        All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday
    }

    /// <summary>
    /// Defines a named maintenance window with day of week and time range constraints
    /// </summary>
    public class MaintenanceWindow
    {
        /// <summary>
        /// Unique identifier for this maintenance window
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Display name for this maintenance window (e.g., "Weekend Evening", "Nightly Maintenance")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional description of when/why this window should be used
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Days of the week this window applies to (flags enum allows multiple days)
        /// </summary>
        public MaintenanceDays Days { get; set; } = MaintenanceDays.All;

        /// <summary>
        /// Start time of the window (local time, 24-hour format as TimeSpan from midnight)
        /// </summary>
        public TimeSpan StartTime { get; set; } = TimeSpan.FromHours(0);

        /// <summary>
        /// End time of the window (local time, 24-hour format as TimeSpan from midnight)
        /// Can be less than StartTime to indicate overnight windows (e.g., 22:00 to 06:00)
        /// </summary>
        public TimeSpan EndTime { get; set; } = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59));

        /// <summary>
        /// If true, this maintenance window is enabled and will be evaluated
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Optional timezone for this window. If null, uses local system time.
        /// </summary>
        public string? TimeZoneId { get; set; }

        /// <summary>
        /// Checks if the given date/time falls within this maintenance window
        /// </summary>
        /// <param name="dateTime">The date/time to check (should be in local time or appropriate timezone)</param>
        /// <returns>True if within the maintenance window</returns>
        public bool IsWithinWindow(DateTimeOffset dateTime)
        {
            if (!IsEnabled)
            {
                return false;
            }

            // Convert to appropriate timezone if specified
            var checkTime = dateTime;
            if (!string.IsNullOrEmpty(TimeZoneId))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
                    checkTime = TimeZoneInfo.ConvertTime(dateTime, tz);
                }
                catch
                {
                    // If timezone lookup fails, use the provided time as-is
                }
            }

            // Check day of week
            var dayFlag = GetDayFlag(checkTime.DayOfWeek);
            if ((Days & dayFlag) == 0)
            {
                return false;
            }

            // Check time of day
            var currentTime = checkTime.TimeOfDay;

            if (EndTime >= StartTime)
            {
                // Normal window (e.g., 09:00 to 17:00)
                return currentTime >= StartTime && currentTime <= EndTime;
            }
            else
            {
                // Overnight window (e.g., 22:00 to 06:00)
                return currentTime >= StartTime || currentTime <= EndTime;
            }
        }

        /// <summary>
        /// Gets the next occurrence of this maintenance window from the given date/time
        /// </summary>
        /// <param name="from">Starting date/time to search from</param>
        /// <returns>The next date/time when this window starts, or null if window is disabled or has no valid days</returns>
        public DateTimeOffset? GetNextOccurrence(DateTimeOffset from)
        {
            if (!IsEnabled || Days == MaintenanceDays.None)
            {
                return null;
            }

            var checkTime = from;
            if (!string.IsNullOrEmpty(TimeZoneId))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
                    checkTime = TimeZoneInfo.ConvertTime(from, tz);
                }
                catch
                {
                    // If timezone lookup fails, use the provided time as-is
                }
            }

            // Check up to 7 days ahead
            for (var i = 0; i < 8; i++)
            {
                var candidateDate = checkTime.Date.AddDays(i);
                var dayFlag = GetDayFlag(candidateDate.DayOfWeek);

                if ((Days & dayFlag) != 0)
                {
                    var windowStart = candidateDate.Add(StartTime);

                    // If this is today and we've already passed the start time, skip to next valid day
                    if (i == 0 && checkTime.TimeOfDay > StartTime)
                    {
                        // Check if we're currently in an overnight window that started yesterday
                        if (EndTime < StartTime && checkTime.TimeOfDay <= EndTime)
                        {
                            // We're currently in the window
                            return new DateTimeOffset(candidateDate.Add(StartTime), checkTime.Offset);
                        }

                        continue;
                    }

                    return new DateTimeOffset(candidateDate.Add(StartTime), checkTime.Offset);
                }
            }

            return null;
        }

        private static MaintenanceDays GetDayFlag(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Sunday => MaintenanceDays.Sunday,
                DayOfWeek.Monday => MaintenanceDays.Monday,
                DayOfWeek.Tuesday => MaintenanceDays.Tuesday,
                DayOfWeek.Wednesday => MaintenanceDays.Wednesday,
                DayOfWeek.Thursday => MaintenanceDays.Thursday,
                DayOfWeek.Friday => MaintenanceDays.Friday,
                DayOfWeek.Saturday => MaintenanceDays.Saturday,
                _ => MaintenanceDays.None
            };
        }

        /// <summary>
        /// Returns a human-readable description of this maintenance window
        /// </summary>
        public string GetScheduleDescription()
        {
            var daysStr = GetDaysDescription();
            var startStr = StartTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            var endStr = EndTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

            if (EndTime < StartTime)
            {
                return $"{daysStr}, {startStr} to {endStr} (overnight)";
            }

            return $"{daysStr}, {startStr} to {endStr}";
        }

        private string GetDaysDescription()
        {
            if (Days == MaintenanceDays.All)
            {
                return "Every day";
            }

            if (Days == MaintenanceDays.Weekdays)
            {
                return "Weekdays";
            }

            if (Days == MaintenanceDays.Weekends)
            {
                return "Weekends";
            }

            if (Days == MaintenanceDays.None)
            {
                return "No days";
            }

            var dayNames = new List<string>();

            if (Days.HasFlag(MaintenanceDays.Sunday))
            {
                dayNames.Add("Sun");
            }

            if (Days.HasFlag(MaintenanceDays.Monday))
            {
                dayNames.Add("Mon");
            }

            if (Days.HasFlag(MaintenanceDays.Tuesday))
            {
                dayNames.Add("Tue");
            }

            if (Days.HasFlag(MaintenanceDays.Wednesday))
            {
                dayNames.Add("Wed");
            }

            if (Days.HasFlag(MaintenanceDays.Thursday))
            {
                dayNames.Add("Thu");
            }

            if (Days.HasFlag(MaintenanceDays.Friday))
            {
                dayNames.Add("Fri");
            }

            if (Days.HasFlag(MaintenanceDays.Saturday))
            {
                dayNames.Add("Sat");
            }

            return string.Join(", ", dayNames);
        }
    }
}
