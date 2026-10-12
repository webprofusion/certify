using System.Text.Json.Serialization;
using Certify.Models.Hub;
using Certify.Server.Hub.Cli.Hub;

namespace Certify.Server.Hub.Cli.Cli
{
    /// <summary>
    /// Source generated serialization for the CLI's own types. Hub API payloads are never bound to types here, they
    /// are handled as JSON documents, which is what keeps the CLI independent of the hub's model assemblies.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(ProfileFile))]
    [JsonSerializable(typeof(LoginRequest))]
    [JsonSerializable(typeof(LocalAdminPrincipalRequest))]
    [JsonSerializable(typeof(LocalAdminCreateAdministratorRequest))]
    [JsonSerializable(typeof(LocalAdminResult))]
    [JsonSerializable(typeof(List<LocalAdminPrincipalSummary>))]
    internal partial class CliJsonContext : JsonSerializerContext
    {
    }

    internal sealed class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
