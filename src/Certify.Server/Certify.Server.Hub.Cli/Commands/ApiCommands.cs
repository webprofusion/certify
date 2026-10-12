using System.CommandLine;
using System.Text.Json;
using Certify.Server.Hub.Cli.Cli;
using Certify.Server.Hub.Cli.Hub;

namespace Certify.Server.Hub.Cli.Commands
{
    /// <summary>
    /// Generic access to any hub API operation, discovered from the hub's OpenAPI document. Responses are written to
    /// stdout as the hub returns them, for tools to consume.
    /// </summary>
    internal static class ApiCommands
    {
        public static Command Create()
        {
            var api = new Command("api", "Call any hub API operation. Operations come from the hub's own OpenAPI document, so they always match the hub version.");

            api.Subcommands.Add(CreateList());
            api.Subcommands.Add(CreateCall());

            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            {
                api.Subcommands.Add(CreateRaw(method));
            }

            return api;
        }

        private static Option<string> CreateDocumentOption()
        {
            var option = new Option<string>("--doc")
            {
                Description = "OpenAPI document: v1 (all), v1-public (/api) or v1-internal (/internal). Internal operations may change between hub versions.",
                DefaultValueFactory = _ => "v1"
            };

            option.AcceptOnlyFromAmong(OpenApiCatalog.DocumentNames);
            return option;
        }

        private static Option<string[]> CreateParameterOption() => new("--param", "-p")
        {
            Description = "Parameter as name=value. Path parameters fill the route, others go in the query string. Repeat for each parameter or array value."
        };

        private static Option<string?> CreateBodyOption() => new("--body")
        {
            Description = "JSON request body: a literal, @file, or - to read stdin."
        };

        private static async Task<List<ApiOperation>> GetOperations(HubConnection connection, string document, CancellationToken cancellationToken)
        {
            var json = await connection.SendAsync(HttpMethod.Get, OpenApiCatalog.GetDocumentPath(document), null, authenticate: false, cancellationToken);
            return OpenApiCatalog.Parse(json);
        }

        private static Command CreateList()
        {
            var document = CreateDocumentOption();
            var filter = new Option<string?>("--filter") { Description = "Only show operations whose id, tag or path contains this text." };
            var output = GlobalOptions.CreateOutputOption();

            var command = new Command("list", "List the hub's API operations.") { document, filter, output };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var connection = GlobalOptions.Connect(parseResult);
                var operations = await GetOperations(connection, parseResult.GetValue(document)!, cancellationToken);

                var text = parseResult.GetValue(filter);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    operations = operations
                        .Where(o => o.QualifiedId.Contains(text, StringComparison.OrdinalIgnoreCase) || o.Path.Contains(text, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (GlobalOptions.IsJsonOutput(parseResult, output))
                {
                    WriteOperationsJson(operations);
                }
                else
                {
                    ConsoleIO.WriteTable(["Operation", "Method", "Path"], operations.Select(o => new[] { o.QualifiedId, o.Method, o.Path }));
                }

                return ExitCodes.Success;
            });
        }

        private static Command CreateCall()
        {
            var operationId = new Argument<string>("operation") { Description = "Operation id, or Tag.OperationId where the id alone is ambiguous (see 'api list')." };
            var parameters = CreateParameterOption();
            var body = CreateBodyOption();
            var document = CreateDocumentOption();

            var command = new Command("call", "Call a hub API operation by its operation id.") { operationId, parameters, body, document };

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var connection = GlobalOptions.Connect(parseResult);
                var operations = await GetOperations(connection, parseResult.GetValue(document)!, cancellationToken);
                var operation = OpenApiCatalog.Resolve(operations, parseResult.GetValue(operationId)!);

                var url = OpenApiCatalog.BuildRelativeUrl(operation.Path, OpenApiCatalog.ParseParameters(parseResult.GetValue(parameters)), operation.Parameters);
                var json = GlobalOptions.ReadBody(parseResult.GetValue(body));

                if (json == null && operation.HasBody && operation.Method is "POST" or "PUT" or "PATCH")
                {
                    ConsoleIO.Info($"Note: {operation.QualifiedId} takes a request body, supply one with --body.");
                }

                return await Send(connection, new HttpMethod(operation.Method), url, json, cancellationToken);
            });
        }

        private static Command CreateRaw(HttpMethod method)
        {
            var path = new Argument<string>("path") { Description = "API path, e.g. /api/v1/system/version" };
            var parameters = CreateParameterOption();
            var body = CreateBodyOption();

            var command = new Command(method.Method.ToLowerInvariant(), $"Send a {method.Method} request to an API path.") { path, parameters };

            if (method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                command.Options.Add(body);
            }

            return command.WithAction(async (parseResult, cancellationToken) =>
            {
                using var connection = GlobalOptions.Connect(parseResult);

                var url = OpenApiCatalog.BuildRelativeUrl(parseResult.GetValue(path)!, OpenApiCatalog.ParseParameters(parseResult.GetValue(parameters)));
                var json = command.Options.Contains(body) ? GlobalOptions.ReadBody(parseResult.GetValue(body)) : null;

                return await Send(connection, method, url, json, cancellationToken);
            });
        }

        private static async Task<int> Send(HubConnection connection, HttpMethod method, string url, string? json, CancellationToken cancellationToken)
        {
            // sent with credentials when there are any, so that anonymous operations can still be called without them
            var authenticate = connection.Settings.AuthMode != HubAuthMode.None;

            var response = await connection.SendAsync(method, url, json, authenticate, cancellationToken);

            if (!string.IsNullOrEmpty(response))
            {
                Console.Out.WriteLine(response);
            }

            return ExitCodes.Success;
        }

        private static void WriteOperationsJson(IEnumerable<ApiOperation> operations)
        {
            using var stream = Console.OpenStandardOutput();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

            writer.WriteStartArray();

            foreach (var operation in operations)
            {
                writer.WriteStartObject();
                writer.WriteString("operationId", operation.OperationId);
                writer.WriteString("tag", operation.Tag);
                writer.WriteString("method", operation.Method);
                writer.WriteString("path", operation.Path);
                writer.WriteStartArray("parameters");

                foreach (var parameter in operation.Parameters)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", parameter.Name);
                    writer.WriteString("in", parameter.In);
                    writer.WriteBoolean("required", parameter.Required);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteBoolean("hasBody", operation.HasBody);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.Flush();
            stream.WriteByte((byte)'\n');
        }
    }
}
