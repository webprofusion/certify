using System;
using System.IO;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class LoggyTests
    {
        private string testsDataPath;
        private string logFilePath;

        [TestInitialize]
        public void TestInitialize()
        {
            testsDataPath = Path.Combine(EnvironmentUtil.CreateAppDataPath(), "Tests");
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
            var log = new Loggy(logImp);

            // Log an error message using Loggy.Error()
            var logMessage = "New Loggy Error";
            log.Error(logMessage);
            logImp.Dispose();

            // Read in logged out error text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out error text
            Assert.IsTrue(logText.Contains(logMessage), $"Logged error message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[ERR]"), "Logged error message should contain '[ERR]'");
        }

        [TestMethod, Description("Test Loggy.Error() Method (Exception)")]
        public void TestLoggyErrorException()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            // Trigger an exception error and log it using Loggy.Error()
            var logMessage = "New Loggy Exception Error";
            var badFilePath = Path.Combine(EnvironmentUtil.CreateAppDataPath(), "Tests", "test1.log");

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
            Assert.IsTrue(logText.Contains(logMessage), $"Logged error message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[ERR]"), "Logged error message should contain '[ERR]'");
            Assert.IsTrue(logText.Contains(exceptionError), $"Logged error message should contain exception error '{exceptionError}'");
        }

        [TestMethod, Description("Test Loggy.Information() Method")]
        public void TestLoggyInformation()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            // Log an info message using Loggy.Information()
            var logMessage = "New Loggy Information";
            log.Information(logMessage);
            logImp.Dispose();

            // Read in logged out info text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out info text
            Assert.IsTrue(logText.Contains(logMessage), $"Logged info message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[INF]"), "Logged info message should contain '[INF]'");
        }

        [TestMethod, Description("Test Loggy.Debug() Method")]
        public void TestLoggyDebug()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            // Log a debug message using Loggy.Debug()
            var logMessage = "New Loggy Debug";
            log.Debug(logMessage);
            logImp.Dispose();

            // Read in logged out debug text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out debug text
            Assert.IsTrue(logText.Contains(logMessage), $"Logged debug message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[DBG]"), "Logged debug message should contain '[DBG]'");
        }

        [TestMethod, Description("Test Loggy.Verbose() Method")]
        public void TestLoggyVerbose()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            // Log a verbose message using Loggy.Verbose()
            var logMessage = "New Loggy Verbose";
            log.Verbose(logMessage);
            logImp.Dispose();

            // Read in logged out verbose text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out verbose text
            Assert.IsTrue(logText.Contains(logMessage), $"Logged verbose message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[VRB]"), "Logged verbose message should contain '[VRB]'");
        }

        [TestMethod, Description("Test Loggy.Warning() Method")]
        public void TestLoggyWarning()
        {
            // Setup instance of Loggy
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            // Log a warning message using Loggy.Warning()
            var logMessage = "New Loggy Warning";
            log.Warning(logMessage);
            logImp.Dispose();

            // Read in logged out warning text
            var logText = File.ReadAllText(this.logFilePath);

            // Validate logged out warning text
            Assert.IsTrue(logText.Contains(logMessage), $"Logged warning message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[WRN]"), "Logged warning message should contain '[WRN]'");
        }
    }
}
