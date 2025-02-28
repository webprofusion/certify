using System.Collections.Generic;
using System.Text;
using Certify.Models;

namespace Certify.UI.Blazor.Core.Models.Services
{
    public class PreviewService
    {
        public static string GetStepsAsMarkdown(IEnumerable<ActionStep> steps)
        {
            var newLine = "\r\n";

            var sb = new StringBuilder();
            foreach (var s in steps)
            {
                sb.AppendLine(newLine + "# " + s.Title);
                sb.AppendLine(s.Description);

                if (s.Substeps != null)
                {
                    foreach (var sub in s.Substeps)
                    {
                        if (!string.IsNullOrEmpty(sub.Description))
                        {
#pragma warning disable CA1847 // Use char literal for a single character lookup
                            if (sub.Description.Contains("|"))
                            {
                                // table items
                                sb.AppendLine(sub.Description);
                            }
                            else if (sub.Description.StartsWith("\r\n", System.StringComparison.Ordinal))
                            {
                                sb.AppendLine(sub.Description);
                            }
                            else
                            {
                                // list items
                                sb.AppendLine(" - " + sub.Description);
                            }
#pragma warning restore CA1847 // Use char literal for a single character lookup
                        }
                        else
                        {
                            sb.AppendLine(" - " + sub.Title);
                        }
                    }
                }
            }

            return sb.ToString();
        }
    }
}
