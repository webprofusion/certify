using System;

namespace Certify.Models.Providers
{
    public interface ILog
    {
        void Warning(string template, params object[] propertyValues);

        void Error(string template, params object[] propertyValues);

        void Error(Exception exp, string template, params object[] propertyValues);

        void Information(string template, params object[] propertyValues);

        void Verbose(string template, params object[] propertyValues);
    }
}