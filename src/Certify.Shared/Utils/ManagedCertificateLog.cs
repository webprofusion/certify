using System;
using System.Collections.Concurrent;
using System.IO;
using Certify.Models.Providers;
using Microsoft.Extensions.Logging;
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
        public DateTimeOffset EventDate { get; set; }
        public string Message { get; set; }
        public LogItemType LogItemType { get; set; }
    }

    public static class ManagedCertificateLog
    {
        private static ConcurrentDictionary<string, Microsoft.Extensions.Logging.ILogger> _managedItemLoggers { get; set; }

        public static string GetLogPath(string managedItemId) => Path.Combine(EnvironmentUtil.CreateAppDataPath("logs"), "log_" + managedItemId.Replace(':', '_') + ".txt");

        public static ILog GetLogger(string managedItemId, LogLevel logLevelSwitch)
        {
            if (string.IsNullOrEmpty(managedItemId))
            {
                return null;
            }

            if (_managedItemLoggers == null)
            {
                _managedItemLoggers = new ConcurrentDictionary<string, Microsoft.Extensions.Logging.ILogger>();
            }

            var log = _managedItemLoggers.GetOrAdd(managedItemId, (key) =>
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

                var serilogLog = new Serilog.LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .WriteTo.File(
                        logPath, shared: true,
                        flushToDiskInterval: new TimeSpan(0, 0, 10)
                    )
                    .CreateLogger();

                return new Serilog.Extensions.Logging.SerilogLoggerFactory(serilogLog).CreateLogger<ManagedCertificate>();

            });

            return new Loggy(log);
        }

        public static void AppendLog(string managedItemId, ManagedCertificateLogItem logItem, LogLevel logLevelSwitch)
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
                    if (l is IDisposable tmp)
                    {
                        tmp?.Dispose();
                    }
                }
            }
        }
    }
}
