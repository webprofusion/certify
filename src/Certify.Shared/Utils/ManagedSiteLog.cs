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

    public class Util
    {
        public const string APPDATASUBFOLDER = "Certify";

        public static string GetAppDataFolder()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\" + APPDATASUBFOLDER;
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
            }
            return path;
        }
    }

    public static class ManagedSiteLog
    {
        private static Dictionary<string, Serilog.Core.Logger> _managedItemLoggers { get; set; }

        public static string GetLogPath(string managedItemId)
        {
            return Util.GetAppDataFolder() + "\\logs\\log_" + managedItemId.Replace(':', '_') + ".txt";
        }

        public static ILogger GetLogger(string managedItemId)
        {
            if (_managedItemLoggers == null) _managedItemLoggers = new Dictionary<string, Serilog.Core.Logger>();

            Serilog.Core.Logger log = null;

            if (_managedItemLoggers.ContainsKey(managedItemId))
            {
                log = _managedItemLoggers[managedItemId];
            }
            else
            {
                var logPath = GetLogPath(managedItemId);

                try
                {
                    if (System.IO.File.Exists(logPath) && new System.IO.FileInfo(logPath).Length > (1024 * 1024))
                    {
                        System.IO.File.Delete(logPath);
                    }
                }
                catch { }

                log = new LoggerConfiguration()
                    .WriteTo.Debug()
                    .WriteTo.File(logPath, shared: true, flushToDiskInterval: new TimeSpan(0, 0, 10))
                    .CreateLogger();

                _managedItemLoggers.Add(managedItemId, log);
            }
            return log;
        }

        public static void AppendLog(string managedItemId, ManagedSiteLogItem logItem)
        {
            var log = GetLogger(managedItemId);

            var logLevel = Serilog.Events.LogEventLevel.Information;

            if (logItem.LogItemType == LogItemType.CertficateRequestFailed) logLevel = Serilog.Events.LogEventLevel.Error;
            if (logItem.LogItemType == LogItemType.GeneralError) logLevel = Serilog.Events.LogEventLevel.Error;
            if (logItem.LogItemType == LogItemType.GeneralWarning) logLevel = Serilog.Events.LogEventLevel.Warning;

            log.Write(logLevel, logItem.Message);
        }

        public static void DisposeLoggers()
        {
            if (_managedItemLoggers.Count > 0)
            {
                foreach (var l in _managedItemLoggers.Values)
                {
                    l.Dispose();
                }
            }
        }
    }
}