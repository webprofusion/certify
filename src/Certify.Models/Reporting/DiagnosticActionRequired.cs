using System;

namespace Certify.Models.Reporting
{
    /// <summary>
    /// A service level diagnostic which needs an operator to intervene, such as the data store being
    /// unreachable. Unlike request progress this is not tied to a particular managed item.
    /// </summary>
    public class DiagnosticActionRequired
    {
        /// <summary>
        /// The system status key this diagnostic relates to, so a client can correlate it with the
        /// corresponding system status item.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Short summary of the condition.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// What went wrong and what the operator needs to do about it.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// True when the service is stopping as a result of this condition, so a client can tell the difference
        /// between a problem it can wait out and one which needs attention before the service will run again.
        /// </summary>
        public bool IsServiceStopping { get; set; }

        public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    }
}
