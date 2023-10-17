using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class LoggyTests
    {
        [TestMethod, Description("Test Loggy.Error() Method")]
        public void TestLoggyError()
        {
            var logFilePath = "C:\\ProgramData\\certify\\Tests\\test.log";
            File.Delete(logFilePath);

            var logImp = new LoggerConfiguration()
                .WriteTo.File(logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Error";
            log.Error(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(logFilePath);

            Assert.IsTrue(logText.Contains(logMessage));
            Assert.IsTrue(logText.Contains("[ERR]"));
            File.Delete(logFilePath);
        }

        [TestMethod, Description("Test Loggy.Error() Method (Exception)")]
        public void TestLoggyErrorException()
        {
            var logFilePath = "C:\\ProgramData\\certify\\Tests\\test.log";
            File.Delete(logFilePath);

            var logImp = new LoggerConfiguration()
                .WriteTo.File(logFilePath)
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

            var logText = File.ReadAllText(logFilePath);

            Assert.IsTrue(logText.Contains(logMessage));
            Assert.IsTrue(logText.Contains("[ERR]"));
            Assert.IsTrue(logText.Contains(exceptionError));
            File.Delete(logFilePath);
        }

        [TestMethod, Description("Test Loggy.Information() Method")]
        public void TestLoggyInformation()
        {
            var logFilePath = "C:\\ProgramData\\certify\\Tests\\test.log";
            File.Delete(logFilePath);

            var logImp = new LoggerConfiguration()
                .WriteTo.File(logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Information";
            log.Information(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(logFilePath);

            Assert.IsTrue(logText.Contains(logMessage));
            Assert.IsTrue(logText.Contains("[INF]"));
            File.Delete(logFilePath);
        }

        [TestMethod, Description("Test Loggy.Debug() Method")]
        public void TestLoggyDebug()
        {
            var logFilePath = "C:\\ProgramData\\certify\\Tests\\test.log";
            File.Delete(logFilePath);

            var logImp = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Debug";
            log.Debug(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(logFilePath);

            Assert.IsTrue(logText.Contains(logMessage));
            Assert.IsTrue(logText.Contains("[DBG]"));
            File.Delete(logFilePath);
        }

        [TestMethod, Description("Test Loggy.Verbose() Method")]
        public void TestLoggyVerbose()
        {
            var logFilePath = "C:\\ProgramData\\certify\\Tests\\test.log";
            File.Delete(logFilePath);

            var logImp = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Verbose";
            log.Verbose(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(logFilePath);

            Assert.IsTrue(logText.Contains(logMessage));
            Assert.IsTrue(logText.Contains("[VRB]"));
            File.Delete(logFilePath);
        }

        [TestMethod, Description("Test Loggy.Warning() Method")]
        public void TestLoggyWarning()
        {
            var logFilePath = "C:\\ProgramData\\certify\\Tests\\test.log";
            File.Delete(logFilePath);

            var logImp = new LoggerConfiguration()
                .WriteTo.File(logFilePath)
                .CreateLogger();
            var log = new Loggy(logImp);

            var logMessage = "New Loggy Warning";
            log.Warning(logMessage);
            logImp.Dispose();

            var logText = File.ReadAllText(logFilePath);

            Assert.IsTrue(logText.Contains(logMessage));
            Assert.IsTrue(logText.Contains("[WRN]"));
            File.Delete(logFilePath);
        }
    }
}
