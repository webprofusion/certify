#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Certify.Models.Hub;

namespace Certify.Shared.Core.Utils
{
    public class LogParsing
    {
        public static LogItem[] ParseLogItems(IEnumerable<string> items)
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

        public static IEnumerable<string> ReadLogTail(string filePath, int lineCount)
        {
            if (lineCount == -1)
            {
                lineCount = int.MaxValue;
            }

            var lines = new LinkedList<string>();
            const int bufferSize = 4096;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var fileLength = fs.Length;
                var position = fileLength;
                var newLineCount = 0;
                var buffer = new byte[bufferSize];
                var currentLine = new List<byte>();

                while (position > 0 && newLineCount < lineCount)
                {
                    var bytesToRead = (int)Math.Min(bufferSize, position);
                    position -= bytesToRead;
                    fs.Seek(position, SeekOrigin.Begin);

                    var totalBytesRead = 0;
                    while (totalBytesRead < bytesToRead)
                    {
                        var bytesRead = fs.Read(buffer, totalBytesRead, bytesToRead - totalBytesRead);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        totalBytesRead += bytesRead;
                    }

                    for (var i = totalBytesRead - 1; i >= 0; i--)
                    {
                        var b = buffer[i];
                        if (b == '\n')
                        {
                            if (currentLine.Count > 0)
                            {
                                currentLine.Reverse();
                                lines.AddFirst(System.Text.Encoding.UTF8.GetString(currentLine.ToArray()));
                                currentLine.Clear();
                                newLineCount++;
                                if (newLineCount == lineCount)
                                {
                                    break;
                                }
                            }
                        }
                        else
                        {
                            currentLine.Add(b);
                        }
                    }
                }

                // Add the last line if needed
                if (currentLine.Count > 0 && newLineCount < lineCount)
                {
                    currentLine.Reverse();
                    lines.AddFirst(System.Text.Encoding.UTF8.GetString(currentLine.ToArray()));
                }
            }

            return lines;
        }
    }
}
