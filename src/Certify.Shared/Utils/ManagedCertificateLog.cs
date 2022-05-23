using System;
using System.Collections.Concurrent;
using System.IO;
using Certify.Models.Providers;
using Serilog;

namespace Certify.Models
{
    public enum LogItemType
    {
        GeneralInfo = 1,
        GeneralWarning = 10,
        GeneralError = 20,
        CertificateRequestStarted = 50,
        CertificateRequestSuccessful = 100,
        CertificateRequestFailed = 101,
        CertificateRequestAttentionRequired = 110
    }

    public class ManagedCertificateLogItem
    {
        public DateTime EventDate { get; set; }
        public string Message { get; set; }
        public LogItemType LogItemType { get; set; }
    }

    public class Util
    {

        public static string GetAppDataFolder()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Models.SharedConstants.APPDATASUBFOLDER);
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
            }

            return path;
        }
    }

    public static class ManagedCertificateLog
    {
        private static ConcurrentDictionary<string, Serilog.Core.Logger> _managedItemLoggers { get; set; }

        public static string GetLogPath(string managedItemId) => Path.Combine(Util.GetAppDataFolder(), "logs", "log_" + managedItemId.Replace(':', '_') + ".txt");

        public static ILog GetLogger(string managedItemId, Serilog.Core.LoggingLevelSwitch logLevelSwitch)
        {
            if (string.IsNullOrEmpty(managedItemId))
            {
                return null;
            }

            if (_managedItemLoggers == null)
            {
                _managedItemLoggers = new ConcurrentDictionary<string, Serilog.Core.Logger>();
            }

            Serilog.Core.Logger log = _managedItemLoggers.GetOrAdd(managedItemId, (key) =>
            {
                var logPath = GetLogPath(key);

                try
                {
                    if (System.IO.File.Exists(logPath) && new System.IO.FileInfo(logPath).Length > (1024 * 1024))
                    {
                        System.IO.File.Delete(logPath);
                    }
                }
                catch { }

                log = new LoggerConfiguration()
                    .MinimumLevel.ControlledBy(logLevelSwitch)
                    .WriteTo.Debug()
                    .WriteTo.File(
                        logPath, shared: true,
                        flushToDiskInterval: new TimeSpan(0, 0, 10)
                    )
                    .CreateLogger();

                return log;
            });

            return new Loggy(log);
        }

        public static void AppendLog(string managedItemId, ManagedCertificateLogItem logItem, Serilog.Core.LoggingLevelSwitch logLevelSwitch)
        {
            var log = GetLogger(managedItemId, logLevelSwitch);

            if (log != null)
            {

                if (logItem.LogItemType == LogItemType.CertificateRequestFailed)
                {
                    log.Error(logItem.Message);
                }
                else if (logItem.LogItemType == LogItemType.GeneralError)
                {
                    log.Error(logItem.Message);
                }
                else if (logItem.LogItemType == LogItemType.GeneralWarning)
                {
                    log.Warning(logItem.Message);
                }
                else
                {
                    log.Information(logItem.Message);
                }
            }
        }

        public static void DisposeLoggers()
        {
            if (_managedItemLoggers?.Count > 0)
            {
                foreach (var l in _managedItemLoggers.Values)
                {
                    l?.Dispose();
                }
            }
        }
    }
}
