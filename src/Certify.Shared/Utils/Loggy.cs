using System;
using Certify.Models.Providers;
using Microsoft.Extensions.Logging;

namespace Certify.Models
{
    public class Loggy : Providers.ILog
    {
        private ILogger _log;

        public Loggy(ILogger log)
        {
            _log = log;
        }

        public void Error(string template, params object[] propertyValues) => _log?.LogError(template, propertyValues);

        public void Error(Exception exp, string template, params object[] propertyValues) => _log?.LogError(exp, template, propertyValues);

        public void Information(string template, params object[] propertyValues) => _log?.LogInformation(template, propertyValues);

        public void Debug(string template, params object[] propertyValues) => _log?.LogDebug(template, propertyValues);

        public void Verbose(string template, params object[] propertyValues) => _log?.LogTrace(template, propertyValues);

        public void Warning(string template, params object[] propertyValues) => _log?.LogWarning(template, propertyValues);
    }

    // Create an adapter class to convert ILog to ILogger
    public class LogToILoggerAdapter : ILogger
    {
        private readonly ILog _log;

        public LogToILoggerAdapter(ILog log)
        {
            _log = log;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, string> formatter)
        {
            var message = formatter(state);

            switch (logLevel)
            {
                case LogLevel.Critical:
                case LogLevel.Error:
                    _log.Error(exception, message);
                    break;
                case LogLevel.Warning:
                    _log.Warning(message);
                    break;
                case LogLevel.Information:
                    _log.Information(message);
                    break;
                case LogLevel.Debug:
                case LogLevel.Trace:
                    _log.Debug(message);
                    break;
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => throw new NotImplementedException();
    }
}
