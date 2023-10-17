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
        private string logFilePath = "C:\\ProgramData\\certify\\Tests\\test.log";

        [TestInitialize]
        public void TestInitialize()
        {
            File.Delete(this.logFilePath);
        }

        [TestCleanup]
        public void TestCleanup() 
        {
            File.Delete(this.logFilePath);
        }

        [TestMethod, Description("Test Loggy.Error() Method")]
        public void TestLoggyError()
        {
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Error";
            log.Error(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(this.logFilePath);

            Assert.IsTrue(logText.Contains(logMessage), $"Logged error message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[ERR]"), "Logged error message should contain '[ERR]'");
        }

        [TestMethod, Description("Test Loggy.Error() Method (Exception)")]
        public void TestLoggyErrorException()
        {
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);
            var logMessage = "New Loggy Exception Error";
            var exceptionError = "System.IO.FileNotFoundException: Could not find file 'C:\\ProgramData\\certify\\Tests\\test1.log'.";

            try
            {
                var badFilePath = "C:\\ProgramData\\certify\\Tests\\test1.log";
                var nullObject = File.ReadAllBytes(badFilePath);
            }
            catch (Exception e)
            {
                log.Error(e, logMessage);
            }

            logImp.Dispose();

            var logText = File.ReadAllText(this.logFilePath);

            Assert.IsTrue(logText.Contains(logMessage), $"Logged error message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[ERR]"), "Logged error message should contain '[ERR]'");
            Assert.IsTrue(logText.Contains(exceptionError), $"Logged error message should contain exception error '{exceptionError}'");
        }

        [TestMethod, Description("Test Loggy.Information() Method")]
        public void TestLoggyInformation()
        {
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Information";
            log.Information(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(this.logFilePath);

            Assert.IsTrue(logText.Contains(logMessage), $"Logged info message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[INF]"), "Logged info message should contain '[INF]'");
        }

        [TestMethod, Description("Test Loggy.Debug() Method")]
        public void TestLoggyDebug()
        {
            var logImp = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Debug";
            log.Debug(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(this.logFilePath);

            Assert.IsTrue(logText.Contains(logMessage), $"Logged debug message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[DBG]"), "Logged debug message should contain '[DBG]'");
        }

        [TestMethod, Description("Test Loggy.Verbose() Method")]
        public void TestLoggyVerbose()
        {
            var logImp = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Verbose";
            log.Verbose(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(this.logFilePath);

            Assert.IsTrue(logText.Contains(logMessage), $"Logged verbose message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[VRB]"), "Logged verbose message should contain '[VRB]'");
        }

        [TestMethod, Description("Test Loggy.Warning() Method")]
        public void TestLoggyWarning()
        {
            var logImp = new LoggerConfiguration()
                .WriteTo.File(this.logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Warning";
            log.Warning(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(this.logFilePath);

            Assert.IsTrue(logText.Contains(logMessage), $"Logged warning message should contain '{logMessage}'");
            Assert.IsTrue(logText.Contains("[WRN]"), "Logged warning message should contain '[WRN]'");
        }
    }
}
