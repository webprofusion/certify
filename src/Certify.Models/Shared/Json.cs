using System.Text.Json;

namespace Certify.Shared
{
    public class JsonOptions
    {
        public static JsonSerializerOptions DefaultJsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
    }
}
