using System.Text.Json;

namespace Certify.Shared
{
    public class JsonOptions
    {
        private static readonly JsonSerializerOptions _defaultJsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

        public static JsonSerializerOptions DefaultJsonSerializerOptions => _defaultJsonSerializerOptions;
    }
}
