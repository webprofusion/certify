using System;

namespace Certify.Models.Hub
{
    public class SystemLogFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string LogType { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTimeOffset DateModified { get; set; }
    }
}
