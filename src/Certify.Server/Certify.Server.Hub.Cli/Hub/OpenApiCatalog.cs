using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Certify.Server.Hub.Cli.Cli;

namespace Certify.Server.Hub.Cli.Hub
{
    internal sealed record ApiParameter(string Name, string In, bool Required);

    internal sealed record ApiOperation(string OperationId, string? Tag, string Method, string Path, IReadOnlyList<ApiParameter> Parameters, bool HasBody)
    {
        /// <summary>
        /// Operation ids are action names and are not unique across controllers, so the tag qualifies them.
        /// </summary>
        public string QualifiedId => string.IsNullOrWhiteSpace(Tag) ? OperationId : $"{Tag}.{OperationId}";
    }

    /// <summary>
    /// The operations a hub offers, read from its own OpenAPI document. Reading them at run time, rather than building
    /// them into the CLI, keeps a separately downloaded CLI in step with whichever hub version it talks to.
    /// </summary>
    internal static partial class OpenApiCatalog
    {
        public static readonly string[] DocumentNames = ["v1", "v1-public", "v1-internal"];

        private static readonly string[] HttpMethods = ["get", "post", "put", "patch", "delete"];

        public static string GetDocumentPath(string documentName) => $"openapi/{documentName}.json";

        public static List<ApiOperation> Parse(string openApiJson)
        {
            using var document = JsonDocument.Parse(openApiJson);

            var operations = new List<ApiOperation>();

            if (!document.RootElement.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
            {
                return operations;
            }

            foreach (var path in paths.EnumerateObject())
            {
                var pathParameters = path.Value.TryGetProperty("parameters", out var shared) ? ReadParameters(shared) : [];

                foreach (var method in HttpMethods)
                {
                    if (!path.Value.TryGetProperty(method, out var operation) || operation.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var operationId = operation.TryGetProperty("operationId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;

                    if (string.IsNullOrWhiteSpace(operationId))
                    {
                        continue;
                    }

                    var tag = operation.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array && tags.GetArrayLength() > 0
                        ? tags[0].GetString()
                        : null;

                    var parameters = pathParameters
                        .Concat(operation.TryGetProperty("parameters", out var own) ? ReadParameters(own) : [])
                        .GroupBy(p => (p.Name, p.In))
                        .Select(g => g.Last())
                        .ToList();

                    operations.Add(new ApiOperation(operationId, tag, method.ToUpperInvariant(), path.Name, parameters, operation.TryGetProperty("requestBody", out _)));
                }
            }

            return operations.OrderBy(o => o.Path, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Method, StringComparer.Ordinal).ToList();
        }

        private static List<ApiParameter> ReadParameters(JsonElement parameters)
        {
            var results = new List<ApiParameter>();

            if (parameters.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var parameter in parameters.EnumerateArray())
            {
                var name = parameter.TryGetProperty("name", out var n) ? n.GetString() : null;
                var location = parameter.TryGetProperty("in", out var i) ? i.GetString() : null;

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                var required = parameter.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
                results.Add(new ApiParameter(name, location, required));
            }

            return results;
        }

        /// <summary>
        /// Find an operation by id, or by Tag.OperationId where the id alone is ambiguous.
        /// </summary>
        public static ApiOperation Resolve(IReadOnlyList<ApiOperation> operations, string id)
        {
            var exact = operations.Where(o => string.Equals(o.QualifiedId, id, StringComparison.OrdinalIgnoreCase)).ToList();

            if (exact.Count == 1)
            {
                return exact[0];
            }

            var byId = operations.Where(o => string.Equals(o.OperationId, id, StringComparison.OrdinalIgnoreCase)).ToList();

            return byId.Count switch
            {
                1 => byId[0],
                0 => throw CliException.Usage($"The hub has no operation '{id}'. Run 'certify-hub api list' to see the available operations."),
                _ => throw CliException.Usage($"'{id}' matches more than one operation, use one of: {string.Join(", ", byId.Select(o => o.QualifiedId))}")
            };
        }

        /// <summary>
        /// Build the request URL: path parameters are substituted into the route and every other parameter is sent in
        /// the query string. A parameter given more than once is sent once per value, which is how arrays are passed.
        /// </summary>
        public static string BuildRelativeUrl(string pathTemplate, IReadOnlyList<KeyValuePair<string, string>> parameters, IReadOnlyList<ApiParameter>? declared = null)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var path = RouteParameterPattern().Replace(pathTemplate, match =>
            {
                var name = match.Groups["name"].Value;
                var optional = match.Groups["optional"].Success
                    || declared?.Any(p => p.In == "path" && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && !p.Required) == true;

                var value = parameters.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase));

                if (value.Key != null)
                {
                    used.Add(name);
                    return Uri.EscapeDataString(value.Value);
                }

                return optional ? string.Empty : throw CliException.Usage($"The parameter '{name}' is required, pass it with -p {name}=<value>.");
            });

            // an omitted optional segment leaves an empty one behind
            path = MultipleSlashPattern().Replace(path, "/");

            if (path.Length > 1)
            {
                path = path.TrimEnd('/');
            }

            var query = new StringBuilder();

            foreach (var parameter in parameters.Where(p => !used.Contains(p.Key)))
            {
                query.Append(query.Length == 0 ? '?' : '&')
                    .Append(Uri.EscapeDataString(parameter.Key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(parameter.Value));
            }

            return path + query;
        }

        /// <summary>
        /// Parse name=value pairs given with -p.
        /// </summary>
        public static List<KeyValuePair<string, string>> ParseParameters(IEnumerable<string>? values)
        {
            var results = new List<KeyValuePair<string, string>>();

            foreach (var value in values ?? [])
            {
                var separator = value.IndexOf('=');

                if (separator <= 0)
                {
                    throw CliException.Usage($"Parameter '{value}' should be in the form name=value.");
                }

                results.Add(new KeyValuePair<string, string>(value[..separator].Trim(), value[(separator + 1)..]));
            }

            return results;
        }

        // route parameters in the forms {name}, {name?} and {name:constraint}
        [GeneratedRegex(@"\{(?<name>[^}:?=]+)(?::[^}?]*)?(?<optional>\?)?\}")]
        private static partial Regex RouteParameterPattern();

        [GeneratedRegex("/{2,}")]
        private static partial Regex MultipleSlashPattern();
    }
}
