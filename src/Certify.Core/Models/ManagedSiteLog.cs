using Certify.Management;
using Serilog;
using System;
using System.Collections.Generic;

namespace Certify.Models
{
    public enum LogItemType
    {
        GeneralInfo = 1,
        GeneralWarning = 10,
        GeneralError = 20,
        CertificateRequestStarted = 50,
        CertificateRequestSuccessful = 100,
        CertficateRequestFailed = 101,
        CertficateRequestAttentionRequired = 110
    }

    public class ManagedSiteLogItem
    {
        public DateTime EventDate { get; set; }
        public string Message { get; set; }
        public LogItemType LogItemType { get; set; }
    }

    public class ManagedSiteLog
    {
        public ManagedSiteLog()
        {
            this.Logs = new List<ManagedSiteLogItem>();
        }

        /// <summary>
        /// Log of recent actions/results for this item 
        /// </summary>
        public List<ManagedSiteLogItem> Logs { get; set; }

        public static string GetLogPath(string managedItemId)
        {
            return Util.GetAppDataFolder() + "\\logs\\log_" + managedItemId.Replace(':', '_') + ".txt";
        }

        public static void AppendLog(string managedItemId, ManagedSiteLogItem logItem)
        {
            //FIXME:
            var logPath = GetLogPath(managedItemId);

            var log = new LoggerConfiguration()
                .WriteTo.File(logPath, shared: true)
                .CreateLogger();

            var logLevel = Serilog.Events.LogEventLevel.Information;
            if (logItem.LogItemType == LogItemType.CertficateRequestFailed) logLevel = Serilog.Events.LogEventLevel.Error;
            if (logItem.LogItemType == LogItemType.GeneralError) logLevel = Serilog.Events.LogEventLevel.Error;
            if (logItem.LogItemType == LogItemType.GeneralWarning) logLevel = Serilog.Events.LogEventLevel.Warning;

            log.Write(logLevel, logItem.Message);
            //TODO: log to per site log
            //if (this.Logs == null) this.Logs = new List<ManagedSiteLogItem>();
            //this.Logs.Add(logItem);
        }
    }
}