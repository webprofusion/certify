using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Certify.Server.HubService.Extensions;

internal static class OpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddConfiguredOpenApiDocuments(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
            ConfigureOpenApiDocument(
                options,
                "Certify Management Hub API",
                "This view contains public and internal APIs. See the public API endpoints view for use in integrations etc. Internal APIs will change without notice."
            ));

        services.AddOpenApi("v1-public", options =>
            ConfigureOpenApiDocument(
                options,
                "Certify Management Hub API - Public Endpoints",
                "Public integration view of the Certify Management Hub API. Includes only routes under /api/.",
                IsPublicOpenApiPath
            ));

        services.AddOpenApi("v1-internal", options =>
            ConfigureOpenApiDocument(
                options,
                "Certify Management Hub API - Internal Endpoints",
                "Internal administration and UI view of the Certify Management Hub API. Includes only routes under /internal/. Internal APIs will change without notice.",
                IsInternalOpenApiPath
            ));

        return services;
    }

    private static bool IsPublicOpenApiPath(string path) => path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

    private static bool IsInternalOpenApiPath(string path) => path.StartsWith("/internal/", StringComparison.OrdinalIgnoreCase);

    private static void ConfigureOpenApiDocument(OpenApiOptions options, string title, string description, Func<string, bool>? includePathPredicate = null)
    {
        options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;

        var info = new OpenApiInfo
        {
            Title = title,
            Version = "v1",
            Description = description
        };

        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info = info;

            ApplyOpenApiSecurity(document);

            if (includePathPredicate != null)
            {
                FilterOpenApiDocument(document, includePathPredicate);
            }

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, cancellationToken) =>
        {
            if (context.Description.ActionDescriptor.RouteValues.TryGetValue("action", out var action))
            {
                operation.OperationId = action;
            }

            if (operation.Security == null || operation.Security.Count == 0)
            {
                operation.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer")] = []
                    },
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("ApiKeyClientId")] = [],
                        [new OpenApiSecuritySchemeReference("ApiKeyClientSecret")] = []
                    }
                ];
            }

            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, cancellationToken) =>
        {
            if (context.JsonTypeInfo.Type == typeof(FileContentResult))
            {
                schema.Type = JsonSchemaType.String;
                schema.Format = "binary";
            }

            return Task.CompletedTask;
        });
    }

    private static void ApplyOpenApiSecurity(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        };

        document.Components.SecuritySchemes["ApiKeyClientId"] = new OpenApiSecurityScheme
        {
            Description = "API Key authentication client ID header",
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Client-ID"
        };

        document.Components.SecuritySchemes["ApiKeyClientSecret"] = new OpenApiSecurityScheme
        {
            Description = "API Key authentication client secret header",
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Client-Secret"
        };

        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            },
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("ApiKeyClientId", document)] = [],
                [new OpenApiSecuritySchemeReference("ApiKeyClientSecret", document)] = []
            }
        ];
    }

    private static void FilterOpenApiDocument(OpenApiDocument document, Func<string, bool> includePathPredicate)
    {
        var filteredPaths = new OpenApiPaths();

        foreach (var path in document.Paths.Where(p => includePathPredicate(p.Key)))
        {
            filteredPaths.Add(path.Key, path.Value);
        }

        document.Paths = filteredPaths;

        FilterOpenApiTags(document);
        FilterOpenApiSchemas(document);
    }

    private static void FilterOpenApiTags(OpenApiDocument document)
    {
        if (document.Tags == null || document.Tags.Count == 0)
        {
            return;
        }

        var usedTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var operation in document.Paths.Values.SelectMany(p => p.Operations.Values))
        {
            if (operation.Tags == null)
            {
                continue;
            }

            foreach (var tag in operation.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag.Name))
                {
                    usedTagNames.Add(tag.Name);
                }
            }
        }

        document.Tags = new HashSet<OpenApiTag>(document.Tags.Where(t => usedTagNames.Contains(t.Name)));
    }

    private static void FilterOpenApiSchemas(OpenApiDocument document)
    {
        if (document.Components?.Schemas == null || document.Components.Schemas.Count == 0)
        {
            return;
        }

        var referencedSchemaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingSchemaIds = new Queue<string>();
        var visitedSchemas = new HashSet<IOpenApiSchema>(OpenApiSchemaReferenceEqualityComparer.Instance);

        foreach (var pathItem in document.Paths.Values)
        {
            CollectOpenApiPathItemSchemaReferences(pathItem, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
        }

        while (pendingSchemaIds.Count > 0)
        {
            var schemaId = pendingSchemaIds.Dequeue();

            if (document.Components.Schemas.TryGetValue(schemaId, out var schema))
            {
                CollectOpenApiSchemaGraphReferences(schema, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
            }
        }

        var filteredSchemas = new Dictionary<string, IOpenApiSchema>(StringComparer.OrdinalIgnoreCase);

        foreach (var schema in document.Components.Schemas)
        {
            if (referencedSchemaIds.Contains(schema.Key))
            {
                filteredSchemas[schema.Key] = schema.Value;
            }
        }

        document.Components.Schemas = filteredSchemas;
    }

    private static void CollectOpenApiPathItemSchemaReferences(IOpenApiPathItem pathItem, HashSet<string> referencedSchemaIds, Queue<string> pendingSchemaIds, HashSet<IOpenApiSchema> visitedSchemas)
    {
        foreach (var operation in pathItem.Operations.Values)
        {
            CollectOpenApiOperationSchemaReferences(operation, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
        }
    }

    private static void CollectOpenApiOperationSchemaReferences(OpenApiOperation operation, HashSet<string> referencedSchemaIds, Queue<string> pendingSchemaIds, HashSet<IOpenApiSchema> visitedSchemas)
    {
        if (operation.Parameters != null)
        {
            foreach (var parameter in operation.Parameters)
            {
                CollectOpenApiSchemaGraphReferences(parameter.Schema, referencedSchemaIds, pendingSchemaIds, visitedSchemas);

                if (parameter.Content != null)
                {
                    foreach (var mediaType in parameter.Content.Values)
                    {
                        CollectOpenApiSchemaGraphReferences(mediaType.Schema, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
                    }
                }
            }
        }

        if (operation.RequestBody?.Content != null)
        {
            foreach (var mediaType in operation.RequestBody.Content.Values)
            {
                CollectOpenApiSchemaGraphReferences(mediaType.Schema, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
            }
        }

        foreach (var response in operation.Responses.Values)
        {
            if (response.Content != null)
            {
                foreach (var mediaType in response.Content.Values)
                {
                    CollectOpenApiSchemaGraphReferences(mediaType.Schema, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
                }
            }

            if (response.Headers != null)
            {
                foreach (var header in response.Headers.Values)
                {
                    CollectOpenApiSchemaGraphReferences(header.Schema, referencedSchemaIds, pendingSchemaIds, visitedSchemas);

                    if (header.Content != null)
                    {
                        foreach (var mediaType in header.Content.Values)
                        {
                            CollectOpenApiSchemaGraphReferences(mediaType.Schema, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
                        }
                    }
                }
            }
        }
    }

    private static void CollectOpenApiSchemaGraphReferences(IOpenApiSchema? schema, HashSet<string> referencedSchemaIds, Queue<string> pendingSchemaIds, HashSet<IOpenApiSchema> visitedSchemas)
    {
        if (schema == null || !visitedSchemas.Add(schema))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(schema.Id) && referencedSchemaIds.Add(schema.Id))
        {
            pendingSchemaIds.Enqueue(schema.Id);
        }

        if (schema.Items != null)
        {
            CollectOpenApiSchemaGraphReferences(schema.Items, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
        }

        if (schema.Properties != null)
        {
            foreach (var property in schema.Properties.Values)
            {
                CollectOpenApiSchemaGraphReferences(property, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
            }
        }

        if (schema.AllOf != null)
        {
            foreach (var item in schema.AllOf)
            {
                CollectOpenApiSchemaGraphReferences(item, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
            }
        }

        if (schema.AnyOf != null)
        {
            foreach (var item in schema.AnyOf)
            {
                CollectOpenApiSchemaGraphReferences(item, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
            }
        }

        if (schema.OneOf != null)
        {
            foreach (var item in schema.OneOf)
            {
                CollectOpenApiSchemaGraphReferences(item, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
            }
        }

        if (schema.Not != null)
        {
            CollectOpenApiSchemaGraphReferences(schema.Not, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
        }

        if (schema.AdditionalProperties != null)
        {
            CollectOpenApiSchemaGraphReferences(schema.AdditionalProperties, referencedSchemaIds, pendingSchemaIds, visitedSchemas);
        }
    }

    private sealed class OpenApiSchemaReferenceEqualityComparer : IEqualityComparer<IOpenApiSchema>
    {
        public static readonly OpenApiSchemaReferenceEqualityComparer Instance = new();

        public bool Equals(IOpenApiSchema? x, IOpenApiSchema? y) => ReferenceEquals(x, y);

        public int GetHashCode(IOpenApiSchema obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
