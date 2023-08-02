using System;
using System.Collections.Generic;
using System.Linq;
using Certify.CertificateAuthorities.Definitions;

namespace Certify.Models.API
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

    public class LogParser
    {
        public static LogItem[] Parse(string[] items)
        {

            var output = new List<LogItem>();

            var logLevelTrim = "] '".ToCharArray();
            var itemSplitChars = "[]".ToCharArray();

            LogItem? unclosedItem = null;
            LogItem? lastItem = null;

            foreach (var item in items)
            {
                var parts = item.Trim().Split(itemSplitChars);
                if (parts.Length >= 3 && DateTime.TryParse($"{parts[0]}", out var eventDate))
                {
                    if (unclosedItem != null)
                    {
                        output.Add(unclosedItem);
                        unclosedItem = null;
                    }

                    lastItem = new LogItem { EventDate = eventDate, LogLevel = parts[1].Trim(logLevelTrim), Message = item.Substring(item.IndexOf(']') + 1) };
                    output.Add(lastItem);

                }
                else
                {
                    // line is probably a continuation
                    if (lastItem != null)
                    {
                        output.Remove(lastItem); // remove so we can re-add the continuation
                        lastItem.Message += $"\n{item}";
                        unclosedItem = lastItem;
                    }
                }
            }

            if (unclosedItem != null)
            {
                if (lastItem != null)
                {
                    output.Remove(lastItem);
                }

                output.Add(unclosedItem);
            }

            return output.ToArray();
        }
    }
}
