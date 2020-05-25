using System;
using Serilog;

namespace Certify.Models
{
    public class Loggy : Providers.ILog
    {
        private ILogger _log;

        public Loggy(ILogger log)
        {
            _log = log;
        }

        public void Error(string template, params object[] propertyValues) => _log.Error(template, propertyValues);

        public void Error(Exception exp, string template, params object[] propertyValues) => _log.Error(exp, template, propertyValues);

        public void Information(string template, params object[] propertyValues) => _log.Information(template, propertyValues);

        public void Debug(string template, params object[] propertyValues) => _log.Debug(template, propertyValues);

        public void Verbose(string template, params object[] propertyValues) => _log.Verbose(template, propertyValues);

        public void Warning(string template, params object[] propertyValues) => _log.Warning(template, propertyValues);
    }
}
