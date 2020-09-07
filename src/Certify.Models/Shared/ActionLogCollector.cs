using System;
using System.Collections.Generic;
using System.Linq;

namespace Certify.Models.Shared
{
    public class ActionLogCollector
    {
        protected List<ActionLogItem> _actionLogs { get; }

        public ActionLogCollector()
        {
            _actionLogs = new List<ActionLogItem>
            {
                Capacity = 1000
            };
        }

        protected void LogAction(string command, string result = null, string managedItemId = null)
        {
            if (_actionLogs != null)
            {
                _actionLogs.Add(new ActionLogItem
                {
                    Command = command,
                    Result = result,
                    ManagedCertificateId = managedItemId,
                    DateTime = DateTime.Now
                });
            }
        }

        public List<string> GetActionLogSummary()
        {
            var output = new List<string>();
            if (_actionLogs != null)
            {
                _actionLogs.ToList().ForEach((a) =>
                {
                    output.Add(a.Command + " : " + (a.Result != null ? a.Result : ""));
                });
            }

            return output;
        }

        public ActionLogItem GetLastActionLogItem() => _actionLogs.LastOrDefault();
    }
}
