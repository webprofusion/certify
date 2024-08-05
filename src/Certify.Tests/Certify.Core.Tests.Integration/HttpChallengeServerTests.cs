using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests
{
    [TestClass]
    public class HttpChallengeServerTests
    {
        private Uri _baseUri = new Uri("http://127.0.0.1:8080/.well-known/acme-challenge/");

        [TestMethod]
        public void TestStartup()
        {
            var challengeServer = new Core.Management.Challenges.HttpChallengeServer();

            var started = challengeServer.Start(new Shared.ServiceConfig { HttpChallengeServerPort = 8080 }, "stop", "configcheck");
            Assert.IsTrue(started, "Http Challenge Server should start");

            Assert.IsTrue(challengeServer.IsRunning, "Http Challenge Server should be running");

            challengeServer.Stop();

            Assert.IsFalse(challengeServer.IsRunning, "Http Challenge Server should not be running");
        }

        [TestMethod]
        public async Task TestControlKey()
        {
            var challengeServer = new Core.Management.Challenges.HttpChallengeServer();

            var started = challengeServer.Start(new Shared.ServiceConfig { HttpChallengeServerPort = 8080 }, "stop", "configcheck");
            Assert.IsTrue(started, "Http Challenge Server should start");

            var client = new HttpClient();
            var result = await client.GetAsync($"{_baseUri}stop");
            if (result.IsSuccessStatusCode)
            {
                var content = await result.Content.ReadAsStringAsync();
                Assert.AreEqual("Stopping", content, "Http Challenge Server should return Stopping");
            }
            else
            {
                Assert.Fail("Http Challenge Server should return OK on configcheck");
            }

            await Task.Delay(1000);

            Assert.IsFalse(challengeServer.IsRunning, "Http Challenge Server should not be running");
        }

        [TestMethod]
        public async Task TestConfigCheck()
        {
            var challengeServer = new Core.Management.Challenges.HttpChallengeServer();
            try
            {
                var started = challengeServer.Start(new Shared.ServiceConfig { HttpChallengeServerPort = 8080 }, "stop", "configcheck", "TESTING");
                Assert.IsTrue(started, "Http Challenge Server should start");

                Assert.IsTrue(challengeServer.IsRunning, "Http Challenge Server should be running");

                var client = new HttpClient();
                var result = await client.GetAsync($"{_baseUri}configcheck");
                if (result.IsSuccessStatusCode)
                {
                    var content = await result.Content.ReadAsStringAsync();
                    Assert.AreEqual("OK", content, "Http Challenge Server should return OK");
                }
                else
                {
                    Assert.Fail("Http Challenge Server should return OK on configcheck");
                }
            }
            finally
            {
                challengeServer.Stop();
            }
        }

        [TestMethod]
        public async Task TestStopOnException()
        {
            var challengeServer = new Core.Management.Challenges.HttpChallengeServer();
            try
            {
                var started = challengeServer.Start(new Shared.ServiceConfig { HttpChallengeServerPort = 8080 }, "stop", "configcheck", "TESTING");
                Assert.IsTrue(started, "Http Challenge Server should start");

                Assert.IsTrue(challengeServer.IsRunning, "Http Challenge Server should be running");

                var client = new HttpClient();
                try
                {
                    await client.GetAsync($"{_baseUri}panic");
                }
                catch
                {
                    // expected
                }

                Assert.IsFalse(challengeServer.IsRunning, "Http Challenge Server should not be running due to previous unhandled exception mid-request");
            }
            finally
            {
                challengeServer.Stop();
            }
        }

        [TestMethod]
        public async Task TestManyRequests()
        {
            var challengeServer = new Core.Management.Challenges.HttpChallengeServer();
            try
            {
                var started = challengeServer.Start(new Shared.ServiceConfig { HttpChallengeServerPort = 8080 }, "stop", "configcheck", "TESTING");

                var client = new HttpClient();
                client.BaseAddress = _baseUri;

                foreach (var i in Enumerable.Range(1, 10000))
                {
                    var result = await client.GetAsync($"test{i}");
                    if (result.IsSuccessStatusCode)
                    {
                        var content = await result.Content.ReadAsStringAsync();
                        Assert.AreEqual("TESTING", content, "Http Challenge Server should return TESTING");
                    }
                    else
                    {
                        Assert.Fail("Http Challenge Server should return value on query");
                    }
                }
            }
            finally
            {
                challengeServer.Stop();
            }
        }
    }
}
