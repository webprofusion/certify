using System;

namespace Certify.Models.Hub
{
    public class LogItem
    {
        public DateTime? EventDate { get; set; }
        public string LogLevel { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
    public class LogResult
    {
        public LogItem[] Items { get; set; } = Array.Empty<LogItem>();
    }
}
