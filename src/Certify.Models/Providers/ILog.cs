using System;

namespace Certify.Models.Providers
{
    public interface ILog
    {
        /// <summary>
        /// Warning level logging
        /// </summary>
        void Warning(string template, params object[] propertyValues);

        /// <summary>
        /// Error level logging
        /// </summary>
        void Error(string template, params object[] propertyValues);

        void Error(Exception exp, string template, params object[] propertyValues);

        /// <summary>
        /// General information level logging
        /// </summary>
        /// <param name="template"></param>
        /// <param name="propertyValues"></param>
        void Information(string template, params object[] propertyValues);

        /// <summary>
        /// Diagnostic level logging
        /// </summary>
        void Debug(string template, params object[] propertyValues);

        /// <summary>
        /// Verbose if the highest logging level and outputs everything
        /// </summary>
        void Verbose(string template, params object[] propertyValues);
    }
}
