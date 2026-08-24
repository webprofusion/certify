using System;
using System.Collections.Generic;
using System.IO;
using Certify.Models;
using Certify.Models.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Certify.Tests.Core.Unit.Tests
{


    /// <summary>
    /// Mock implementation of ILog for testing
    /// </summary>
    public class MockLog : ILog
    {
        public List<string> LogEntries { get; } = new List<string>();

        public void Verbose(string template, params object[] propertyValues) => LogEntries.Add($"VERBOSE: {string.Format(template, propertyValues)}");
        public void Debug(string template, params object[] propertyValues) => LogEntries.Add($"DEBUG: {string.Format(template, propertyValues)}");
        public void Information(string template, params object[] propertyValues) => LogEntries.Add($"INFO: {string.Format(template, propertyValues)}");
        public void Warning(string template, params object[] propertyValues) => LogEntries.Add($"WARNING: {string.Format(template, propertyValues)}");
        public void Error(string template, params object[] propertyValues) => LogEntries.Add($"ERROR: {string.Format(template, propertyValues)}");
        public void Error(Exception ex, string template, params object[] propertyValues) => LogEntries.Add($"ERROR: {string.Format(template, propertyValues)} - {ex.Message}");
    }

    [TestClass]
    public class LoggyTests
    {
        private string testsDataPath;
        private string logFilePath;

        [TestInitialize]
        public void TestInitialize()
        {
            testsDataPath = Path.Combine(EnvironmentUtil.EnsuredAppDataPath(), "Tests");
            logFilePath = Path.Combine(testsDataPath, "test.log");

            if (!Directory.Exists(testsDataPath))
            {
                Directory.CreateDirectory(testsDataPath);

            }

            if (File.Exists(logFilePath))
            {
                File.Delete(this.logFilePath);
            }
        }

        [TestCleanup]
        public void TestCleanup()
        {
            File.Delete(this.logFilePath);
        }

        [TestMethod, Description("Test Loggy.Error() Method")]
        public void TestLoggyError()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();

            var log = new Loggy(new Serilog.Extensions.Logging.SerilogLoggerFactory(logImp).CreateLogger<LoggyTests>());

            // Log an error message using Loggy.Error()
            var logMessage = "New Loggy Error";
            log.Error(logMessage);
            logImp.Dispose();

            // Read in logged out error text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out error text
            Assert.Contains(logMessage, logText, $"Logged error message should contain '{logMessage}'");
            Assert.Contains("[ERR]", logText, "Logged error message should contain '[ERR]'");
        }

        [TestMethod, Description("Test Loggy.Error() Method (Exception)")]
        public void TestLoggyErrorException()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(new Serilog.Extensions.Logging.SerilogLoggerFactory(logImp).CreateLogger<LoggyTests>());

            // Trigger an exception error and log it using Loggy.Error()
            var logMessage = "New Loggy Exception Error";
            var badFilePath = Path.Combine(EnvironmentUtil.EnsuredAppDataPath(), "Tests", "test1.log");

            var exceptionError = $"System.IO.FileNotFoundException: Could not find file '{badFilePath}'.";
            try
            {
                var nullObject = File.ReadAllBytes(badFilePath);
            }
            catch (Exception e)
            {
                log.Error(e, logMessage);
            }

            logImp.Dispose();

            // Read in logged out exception error text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out exception error text
            Assert.Contains(logMessage, logText, $"Logged error message should contain '{logMessage}'");
            Assert.Contains("[ERR]", logText, "Logged error message should contain '[ERR]'");
            Assert.Contains(exceptionError, logText, $"Logged error message should contain exception error '{exceptionError}'");
        }

        [TestMethod, Description("Test Loggy.Information() Method")]
        public void TestLoggyInformation()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(new Serilog.Extensions.Logging.SerilogLoggerFactory(logImp).CreateLogger<LoggyTests>());

            // Log an info message using Loggy.Information()
            var logMessage = "New Loggy Information";
            log.Information(logMessage);
            logImp.Dispose();

            // Read in logged out info text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out info text
            Assert.Contains(logMessage, logText, $"Logged info message should contain '{logMessage}'");
            Assert.Contains("[INF]", logText, "Logged info message should contain '[INF]'");
        }

        [TestMethod, Description("Test Loggy.Debug() Method")]
        public void TestLoggyDebug()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(new Serilog.Extensions.Logging.SerilogLoggerFactory(logImp).CreateLogger<LoggyTests>());

            // Log a debug message using Loggy.Debug()
            var logMessage = "New Loggy Debug";
            log.Debug(logMessage);
            logImp.Dispose();

            // Read in logged out debug text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out debug text
            Assert.Contains(logMessage, logText, $"Logged debug message should contain '{logMessage}'");
            Assert.Contains("[DBG]", logText, "Logged debug message should contain '[DBG]'");
        }

        [TestMethod, Description("Test Loggy.Verbose() Method")]
        public void TestLoggyVerbose()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(new Serilog.Extensions.Logging.SerilogLoggerFactory(logImp).CreateLogger<LoggyTests>());

            // Log a verbose message using Loggy.Verbose()
            var logMessage = "New Loggy Verbose";
            log.Verbose(logMessage);
            logImp.Dispose();

            // Read in logged out verbose text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out verbose text
            Assert.Contains(logMessage, logText, $"Logged verbose message should contain '{logMessage}'");
            Assert.Contains("[VRB]", logText, "Logged verbose message should contain '[VRB]'");
        }

        [TestMethod, Description("Test Loggy.Warning() Method")]
        public void TestLoggyWarning()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();

            var log = new Loggy(new Serilog.Extensions.Logging.SerilogLoggerFactory(logImp).CreateLogger<LoggyTests>());

            // Log a warning message using Loggy.Warning()
            var logMessage = "New Loggy Warning";
            log.Warning(logMessage);
            logImp.Dispose();

            // Read in logged out warning text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out warning text
            Assert.Contains(logMessage, logText, $"Logged warning message should contain '{logMessage}'");
            Assert.Contains("[WRN]", logText, "Logged warning message should contain '[WRN]'");
        }
    }
}
