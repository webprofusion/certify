using System;
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
}
