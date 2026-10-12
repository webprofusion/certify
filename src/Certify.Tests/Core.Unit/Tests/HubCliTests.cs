extern alias hubcli;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using hubcli::Certify.Server.Hub.Cli.Cli;
using hubcli::Certify.Server.Hub.Cli.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The hub CLI resolves which hub and credentials to use, and turns hub OpenAPI operations into requests.
    /// </summary>
    [TestClass]
    public class HubCliTests
    {
        private const string HubUrl = "https://hub.example.com:9696/";

        private static Func<string, string?> Env(Dictionary<string, string>? values = null) => name => values != null && values.TryGetValue(name, out var v) ? v : null;

        private static Func<string, HubProfile?> Profiles(HubProfile? profile = null) => _ => profile;

        private static HubProfile SignedInProfile(string url = HubUrl) => new() { Url = url, Username = "admin", AccessToken = "access", RefreshToken = "refresh" };

        [TestMethod]
        public void CommandLineApiTokenTakesPrecedenceOverEnvironmentAndProfile()
        {
            var settings = HubSettings.Resolve(
                new HubCliInputs { Url = HubUrl, ClientId = "cli-id", ClientSecret = "cli-secret" },
                Env(new() { [HubSettings.ClientIdVariable] = "env-id", [HubSettings.ClientSecretVariable] = "env-secret" }),
                Profiles(SignedInProfile()));

            Assert.AreEqual(HubAuthMode.ApiToken, settings.AuthMode);
            Assert.AreEqual("cli-id", settings.ClientId);
            Assert.AreEqual("cli-secret", settings.ClientSecret);
        }

        [TestMethod]
        public void EnvironmentApiTokenTakesPrecedenceOverASavedSignIn()
        {
            var settings = HubSettings.Resolve(
                new HubCliInputs(),
                Env(new() { [HubSettings.UrlVariable] = HubUrl, [HubSettings.ClientIdVariable] = "env-id", [HubSettings.ClientSecretVariable] = "env-secret" }),
                Profiles(SignedInProfile()));

            Assert.AreEqual(HubAuthMode.ApiToken, settings.AuthMode);
            Assert.AreEqual("env-id", settings.ClientId);
        }

        [TestMethod]
        public void ApiTokenIdAndSecretMustComeFromTheSameSource()
        {
            // a command line id must not pair with a secret left in the environment
            var ex = Assert.ThrowsExactly<CliException>(() => HubSettings.Resolve(
                new HubCliInputs { Url = HubUrl, ClientId = "cli-id" },
                Env(new() { [HubSettings.ClientSecretVariable] = "env-secret" }),
                Profiles()));

            Assert.AreEqual(ExitCodes.Usage, ex.ExitCode);
        }

        [TestMethod]
        public void SavedSignInIsUsedForItsOwnHubOnly()
        {
            var sameHub = HubSettings.Resolve(new HubCliInputs { Url = "https://HUB.example.com:9696" }, Env(), Profiles(SignedInProfile()));
            Assert.AreEqual(HubAuthMode.Login, sameHub.AuthMode, "The profile's hub, given without the trailing slash or in another case, is the same hub.");

            var otherHub = HubSettings.Resolve(new HubCliInputs { Url = "https://other.example.com" }, Env(), Profiles(SignedInProfile()));
            Assert.AreEqual(HubAuthMode.None, otherHub.AuthMode, "A session must never be sent to a different hub.");

            var fromProfile = HubSettings.Resolve(new HubCliInputs(), Env(), Profiles(SignedInProfile()));
            Assert.AreEqual(new Uri(HubUrl), fromProfile.BaseUri, "The profile's hub is used when no other is given.");
            Assert.AreEqual(HubAuthMode.Login, fromProfile.AuthMode);
        }

        [TestMethod]
        public void HubUrlIsRequiredAndValidated()
        {
            Assert.AreEqual(ExitCodes.Usage, Assert.ThrowsExactly<CliException>(() => HubSettings.Resolve(new HubCliInputs(), Env(), Profiles())).ExitCode);
            Assert.AreEqual(ExitCodes.Usage, Assert.ThrowsExactly<CliException>(() => HubSettings.Resolve(new HubCliInputs { Url = "ftp://hub" }, Env(), Profiles())).ExitCode);

            Assert.AreEqual("https://hub.example.com/prefix/", HubSettings.ParseHubUrl("https://hub.example.com/prefix?x=1").ToString());
        }

        private const string OpenApiDocument = """
            {
              "openapi": "3.0.1",
              "paths": {
                "/internal/v1/certificate/{instanceId}/download/{managedCertId}/{format}": {
                  "parameters": [ { "name": "instanceId", "in": "path", "required": true } ],
                  "get": {
                    "tags": [ "Certificate" ],
                    "operationId": "Download",
                    "parameters": [
                      { "name": "managedCertId", "in": "path", "required": true },
                      { "name": "format", "in": "path", "required": false }
                    ]
                  }
                },
                "/internal/v1/hub/items": {
                  "get": { "tags": [ "Hub" ], "operationId": "GetItems", "parameters": [ { "name": "keyword", "in": "query" } ] }
                },
                "/internal/v1/tag": {
                  "get": { "tags": [ "Tag" ], "operationId": "GetItems" },
                  "post": { "tags": [ "Tag" ], "operationId": "AddTag", "requestBody": { "content": { } } }
                }
              }
            }
            """;

        [TestMethod]
        public void OperationsAreReadFromTheOpenApiDocument()
        {
            var operations = OpenApiCatalog.Parse(OpenApiDocument);

            Assert.HasCount(4, operations);

            var download = operations.Single(o => o.OperationId == "Download");
            Assert.AreEqual("GET", download.Method);
            Assert.AreEqual("Certificate.Download", download.QualifiedId);
            Assert.HasCount(3, download.Parameters, "Path level parameters apply to the operation as well as its own.");

            Assert.IsTrue(operations.Single(o => o.OperationId == "AddTag").HasBody);
        }

        [TestMethod]
        public void AmbiguousOperationIdsNeedTheTag()
        {
            var operations = OpenApiCatalog.Parse(OpenApiDocument);

            var ex = Assert.ThrowsExactly<CliException>(() => OpenApiCatalog.Resolve(operations, "GetItems"));
            Assert.AreEqual(ExitCodes.Usage, ex.ExitCode);
            StringAssert.Contains(ex.Message, "Hub.GetItems");
            StringAssert.Contains(ex.Message, "Tag.GetItems");

            Assert.AreEqual("/internal/v1/tag", OpenApiCatalog.Resolve(operations, "tag.getitems").Path);
            Assert.AreEqual("Download", OpenApiCatalog.Resolve(operations, "download").OperationId);
            Assert.ThrowsExactly<CliException>(() => OpenApiCatalog.Resolve(operations, "NoSuchOperation"));
        }

        [TestMethod]
        public void PathParametersFillTheRouteAndOthersGoInTheQuery()
        {
            var download = OpenApiCatalog.Resolve(OpenApiCatalog.Parse(OpenApiDocument), "Download");

            var url = OpenApiCatalog.BuildRelativeUrl(download.Path, OpenApiCatalog.ParseParameters(["instanceId=abc", "managedCertId=a b/c", "format=pfx", "tag=x", "tag=y=z"]), download.Parameters);
            Assert.AreEqual("/internal/v1/certificate/abc/download/a%20b%2Fc/pfx?tag=x&tag=y%3Dz", url);

            var withoutOptional = OpenApiCatalog.BuildRelativeUrl(download.Path, OpenApiCatalog.ParseParameters(["instanceId=abc", "managedCertId=m1"]), download.Parameters);
            Assert.AreEqual("/internal/v1/certificate/abc/download/m1", withoutOptional, "An optional path parameter which is not given is left out.");

            var ex = Assert.ThrowsExactly<CliException>(() => OpenApiCatalog.BuildRelativeUrl(download.Path, OpenApiCatalog.ParseParameters(["instanceId=abc"]), download.Parameters));
            StringAssert.Contains(ex.Message, "managedCertId");

            Assert.AreEqual("/a/1/b", OpenApiCatalog.BuildRelativeUrl("/a/{id:int}/b", [new("id", "1")]), "Route constraints are not part of the parameter name.");
            Assert.ThrowsExactly<CliException>(() => OpenApiCatalog.ParseParameters(["novalue"]));
        }

        [TestMethod]
        public void SessionTokensAndExpiryAreReadFromHubResponses()
        {
            var session = HubConnection.ParseSession("""{ "isSuccess": true, "accessToken": "a.b.c", "refreshToken": "r1", "securityPrincipal": { "username": "admin" } }""");

            Assert.IsNotNull(session);
            Assert.AreEqual("a.b.c", session.AccessToken);
            Assert.AreEqual("r1", session.RefreshToken);
            Assert.AreEqual("admin", session.Username);

            Assert.IsNull(HubConnection.ParseSession("""{ "isSuccess": false }"""), "A response without both tokens is not a session.");
            Assert.IsNull(HubConnection.ParseSession("not json"));

            var expiry = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"sub\":\"admin_01\",\"exp\":{expiry}}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            Assert.AreEqual(expiry, HubConnection.GetExpiry($"header.{payload}.signature")?.ToUnixTimeSeconds());
            Assert.IsNull(HubConnection.GetExpiry("not-a-jwt"));
        }
    }
}
